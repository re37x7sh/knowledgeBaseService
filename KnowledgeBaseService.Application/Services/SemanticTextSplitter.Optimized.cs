using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KnowledgeBaseService.Application.DTOs;
using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Application.Options;
using Microsoft.Extensions.Logging;

namespace KnowledgeBaseService.Application.Services;

/// <summary>
/// 生产级语义文本分割器实现
/// 包含所有优化：滑动窗口、缓存、重叠、段落感知等
/// </summary>
public class SemanticTextSplitterOptimized : ISemanticTextSplitter
{
    private readonly IEmbeddingClient _embeddingClient;
    private readonly ICacheService? _cacheService;
    private readonly ILogger<SemanticTextSplitterOptimized> _logger;

    public SemanticTextSplitterOptimized(
        IEmbeddingClient embeddingClient,
        ICacheService? cacheService,
        ILogger<SemanticTextSplitterOptimized> logger)
    {
        _embeddingClient = embeddingClient ?? throw new ArgumentNullException(nameof(embeddingClient));
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <summary>
    /// 简化版本：向后兼容
    /// </summary>
    public async Task<List<string>> SplitAsync(
        string text,
        double similarityThreshold = 0.65,
        int maxChunkSize = 1500,
        CancellationToken cancellationToken = default)
    {
        var options = new SemanticChunkingOptions
        {
            SimilarityThreshold = similarityThreshold,
            MaxChunkSize = maxChunkSize
        };

        var (chunks, _) = await SplitWithMetricsAsync(text, options, cancellationToken);
        return chunks;
    }

    /// <summary>
    /// 完整版本：带质量指标
    /// </summary>
    public async Task<(List<string> Chunks, ChunkQualityMetrics Metrics)> SplitWithMetricsAsync(
        string text,
        SemanticChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        options ??= new SemanticChunkingOptions();

        var metrics = new ChunkQualityMetrics();
        int retryCount = 0;

        try
        {
            if (string.IsNullOrWhiteSpace(text))
                return (new List<string>(), metrics);

            _logger.LogInformation("开始语义分块: 阈值={Threshold}, 最大块={MaxSize}, 窗口={Window}",
                options.SimilarityThreshold, options.MaxChunkSize, options.WindowSize);

            // 1. 段落感知分割
            var paragraphs = options.EnableParagraphAware
                ? SplitIntoParagraphs(text)
                : new List<string> { text };

            _logger.LogDebug("文档已分割为 {Count} 个段落", paragraphs.Count);

            var allChunks = new List<string>();
            var allEmbeddings = new List<float[]>();
            var allSentences = new List<string>();
            var cacheHits = 0;
            var totalSentences = 0;

            // 2. 处理每个段落
            foreach (var paragraph in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(paragraph))
                    continue;

                var sentences = SplitIntoSentences(paragraph);
                if (sentences.Count == 0)
                    continue;

                totalSentences += sentences.Count;

                // 段落太短，直接作为整体
                if (sentences.Count <= options.MinSentencesPerChunk)
                {
                    allChunks.Add(paragraph.Trim());
                    continue;
                }

                // 3. 批量生成 Embedding（带缓存）
                var embeddingStopwatch = Stopwatch.StartNew();
                var (embeddings, hits) = await GetEmbeddingsWithCacheAsync(
                    sentences, options, cancellationToken);
                cacheHits += hits;

                if (embeddings.Length != sentences.Count || embeddings.Any(e => e.Length == 0))
                {
                    _logger.LogWarning("部分 embedding 生成失败，回退到字符分块");
                    var fallbackChunks = FallbackSplit(paragraph, options.MaxChunkSize);
                    allChunks.AddRange(fallbackChunks);
                    continue;
                }

                allEmbeddings.AddRange(embeddings);
                allSentences.AddRange(sentences);
                embeddingStopwatch.Stop();

                metrics.EmbeddingTimeMs += embeddingStopwatch.ElapsedMilliseconds;
            }

            metrics.TotalSentences = totalSentences;
            metrics.CacheHits = cacheHits;
            metrics.CacheHitRate = totalSentences > 0 ? (double)cacheHits / totalSentences : 0;

            // 4. 并行计算相似度
            var mergingStopwatch = Stopwatch.StartNew();
            var chunks = MergeSentencesIntoChunks(
                allSentences,
                allEmbeddings.ToArray(),
                options,
                metrics);

            allChunks.AddRange(chunks);
            mergingStopwatch.Stop();

            metrics.MergingTimeMs = mergingStopwatch.ElapsedMilliseconds;
            metrics.RetryCount = retryCount;

            stopwatch.Stop();
            metrics.TotalProcessingTimeMs = stopwatch.ElapsedMilliseconds;

            // 5. 计算质量指标
            CalculateQualityMetrics(allChunks, metrics);

            _logger.LogInformation("语义分块完成: 原文 {Length} 字符 -> {Count} 个块, 耗时 {Ms}ms",
                text.Length, allChunks.Count, stopwatch.ElapsedMilliseconds);

            return (allChunks, metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "语义分块失败，回退到字符分块");
            var fallbackChunks = FallbackSplit(text, options.MaxChunkSize);
            stopwatch.Stop();
            metrics.TotalProcessingTimeMs = stopwatch.ElapsedMilliseconds;
            return (fallbackChunks, metrics);
        }
    }

    #region Embedding 生成（带缓存和重试）

    private async Task<(float[][] Embeddings, int CacheHits)> GetEmbeddingsWithCacheAsync(
        List<string> sentences,
        SemanticChunkingOptions options,
        CancellationToken cancellationToken)
    {
        var sentenceHashes = sentences.Select(s => ComputeHash(s)).ToList();
        var cachedEmbeddings = new float[sentences.Count][];
        var uncachedIndices = new List<int>();
        int cacheHits = 0;

        // 1. 尝试从缓存获取
        if (options.EnableCaching && _cacheService != null)
        {
            var cacheKeys = uncachedIndices.Select(i => $"embedding:sentence:{sentenceHashes[i]}").ToArray();
            var cachedResults = await _cacheService.GetManyAsync<float[]>(cacheKeys, cancellationToken);

            for (int i = 0; i < sentences.Count; i++)
            {
                var cacheKey = $"embedding:sentence:{sentenceHashes[i]}";
                if (cachedResults.TryGetValue(cacheKey, out var cached))
                {
                    cachedEmbeddings[i] = cached;
                    cacheHits++;
                }
                else
                {
                    uncachedIndices.Add(i);
                }
            }

            _logger.LogDebug("缓存命中: {Hits}/{Total}", cacheHits, sentences.Count);
        }
        else
        {
            // 缓存未启用，全部需要生成
            for (int i = 0; i < sentences.Count; i++)
                uncachedIndices.Add(i);
        }

        // 2. 批量生成未缓存的 embeddings（带重试）
        if (uncachedIndices.Count > 0)
        {
            var newEmbeddings = await GetEmbeddingsWithRetryAsync(
                uncachedIndices.Select(i => sentences[i]).ToList(),
                options,
                cancellationToken);

            for (int i = 0; i < uncachedIndices.Count; i++)
            {
                var idx = uncachedIndices[i];
                cachedEmbeddings[idx] = newEmbeddings[i];

                // 写入缓存
                if (options.EnableCaching && _cacheService != null)
                {
                    var cacheKey = $"embedding:sentence:{sentenceHashes[idx]}";
                    await _cacheService.SetAsync(
                        cacheKey,
                        newEmbeddings[i],
                        TimeSpan.FromDays(options.CacheTTLDays),
                        cancellationToken);
                }
            }
        }

        return (cachedEmbeddings, cacheHits);
    }

    private async Task<float[][]> GetEmbeddingsWithRetryAsync(
        List<string> sentences,
        SemanticChunkingOptions options,
        CancellationToken cancellationToken)
    {
        var allEmbeddings = new List<float[]>();
        int retryCount = 0;

        // 分批处理
        for (int i = 0; i < sentences.Count; i += options.MaxBatchSize)
        {
            var batch = sentences.Skip(i).Take(options.MaxBatchSize).ToList();
            bool success = false;

            while (!success && retryCount < options.MaxRetries)
            {
                try
                {
                    var results = await _embeddingClient.GetEmbeddingsAsync(batch, cancellationToken);
                    var embeddings = results.Select(r => r.Vector ?? Array.Empty<float>()).ToArray();
                    allEmbeddings.AddRange(embeddings);
                    success = true;
                }
                catch (Exception ex) when (retryCount < options.MaxRetries - 1)
                {
                    retryCount++;
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                    _logger.LogWarning(ex, "Embedding 批次失败，{Delay}秒后重试 ({Retry}/{MaxRetries})",
                        delay.TotalSeconds, retryCount, options.MaxRetries);
                    await Task.Delay(delay, cancellationToken);
                }
            }

            if (!success)
            {
                throw new InvalidOperationException($"批次 embedding 生成失败");
            }
        }

        return allEmbeddings.ToArray();
    }

    #endregion

    #region 分句与分块逻辑

    private List<string> SplitIntoParagraphs(string text)
    {
        var separators = new[] { "\n\n", "\r\n\r\n", "\n\r\n\r" };
        var paragraphs = text.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        return paragraphs.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }

    private List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var pattern = new Regex(
            @"[^。！？.!?]+[。！？.!?]?(?=\s|$|[""”）]})]|$)",
            RegexOptions.Multiline | RegexOptions.Compiled);

        foreach (Match match in pattern.Matches(text))
        {
            var sentence = match.Value.Trim();
            if (sentence.Length > 0)
                sentences.Add(sentence);
        }

        return sentences.Count > 0 ? sentences : SimpleSentenceSplit(text);
    }

    private List<string> SimpleSentenceSplit(string text)
    {
        var sentences = new List<string>();
        var separators = new[] { "。", "！", "？", ".", "!", "?" };
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (separators.Contains(text[i].ToString()))
            {
                var sentence = text.Substring(start, i - start + 1).Trim();
                if (sentence.Length > 0)
                    sentences.Add(sentence);
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            var remaining = text.Substring(start).Trim();
            if (remaining.Length > 0)
                sentences.Add(remaining);
        }

        return sentences.Count > 0 ? sentences : new List<string> { text };
    }

    private List<string> MergeSentencesIntoChunks(
        List<string> sentences,
        float[][] embeddings,
        SemanticChunkingOptions options,
        ChunkQualityMetrics metrics)
    {
        if (sentences.Count == 0)
            return new List<string>();

        // 计算所有相似度
        var similarities = options.EnableParallelProcessing
            ? CalculateAllSimilaritiesParallel(embeddings, options.WindowSize)
            : CalculateAllSimilaritiesSequential(embeddings, options.WindowSize);

        var avgSimilarity = similarities.Average();
        metrics.AverageSimilarity = avgSimilarity;
        metrics.StdDevSimilarity = CalculateStdDev(similarities.Select(s => (double)s), avgSimilarity);
        metrics.LowSimilaritySplits = similarities.Count(s => s < 0.5f);

        // 合并句子成块
        var chunks = new List<string>();
        var currentChunkSentences = new List<string> { sentences[0] };
        int currentChunkSize = sentences[0].Length;

        for (int i = 1; i < sentences.Count; i++)
        {
            var similarity = similarities[i - 1];
            var newChunkSize = currentChunkSize + sentences[i].Length;

            bool shouldSplit = similarity < options.SimilarityThreshold ||
                             newChunkSize > options.MaxChunkSize ||
                             currentChunkSentences.Count >= options.MaxSentencesPerChunk;

            if (shouldSplit)
            {
                // 检查是否太小
                if (currentChunkSentences.Count < options.MinSentencesPerChunk &&
                    currentChunkSize < options.MinChunkSize &&
                    chunks.Count > 0)
                {
                    // 合并到上一个块
                    var lastChunk = chunks[chunks.Count - 1];
                    chunks[chunks.Count - 1] = lastChunk + string.Join("", currentChunkSentences);
                }
                else
                {
                    chunks.Add(string.Join("", currentChunkSentences));
                }

                // 开始新块（带重叠）
                int overlapCount = Math.Min(options.OverlapSentences, currentChunkSentences.Count);
                currentChunkSentences = currentChunkSentences.TakeLast(overlapCount).ToList();
                currentChunkSentences.Add(sentences[i]);
                currentChunkSize = currentChunkSentences.Sum(s => s.Length);
            }
            else
            {
                currentChunkSentences.Add(sentences[i]);
                currentChunkSize = newChunkSize;
            }
        }

        // 添加最后一个块
        if (currentChunkSentences.Count > 0)
        {
            chunks.Add(string.Join("", currentChunkSentences));
        }

        return chunks;
    }

    #endregion

    #region 相似度计算

    private float[] CalculateAllSimilaritiesSequential(float[][] embeddings, int windowSize)
    {
        var similarities = new float[embeddings.Length - 1];

        for (int i = 1; i < embeddings.Length; i++)
        {
            similarities[i - 1] = CalculateWindowSimilarity(embeddings, i, windowSize);
        }

        return similarities;
    }

    private float[] CalculateAllSimilaritiesParallel(float[][] embeddings, int windowSize)
    {
        var similarities = new float[embeddings.Length - 1];

        Parallel.For(1, embeddings.Length, i =>
        {
            similarities[i - 1] = CalculateWindowSimilarity(embeddings, i, windowSize);
        });

        return similarities;
    }

    private float CalculateWindowSimilarity(float[][] embeddings, int currentIndex, int windowSize)
    {
        int start = Math.Max(0, currentIndex - windowSize + 1);
        int end = Math.Min(embeddings.Length, currentIndex + windowSize);

        float totalSimilarity = 0;
        int count = 0;

        for (int i = start; i < end; i++)
        {
            if (i != currentIndex)
            {
                totalSimilarity += CosineSimilarity(embeddings[currentIndex], embeddings[i]);
                count++;
            }
        }

        return count > 0 ? totalSimilarity / count : 0;
    }

    private static float CosineSimilarity(float[] vec1, float[] vec2)
    {
        if (vec1.Length != vec2.Length)
            return 0f;

        float dotProduct = 0;
        float magnitude1 = 0;
        float magnitude2 = 0;

        for (int i = 0; i < vec1.Length; i++)
        {
            dotProduct += vec1[i] * vec2[i];
            magnitude1 += vec1[i] * vec1[i];
            magnitude2 += vec2[i] * vec2[i];
        }

        magnitude1 = (float)Math.Sqrt(magnitude1);
        magnitude2 = (float)Math.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0f;

        return dotProduct / (magnitude1 * magnitude2);
    }

    #endregion

    #region 工具方法

    private List<string> FallbackSplit(string text, int maxChunkSize)
    {
        var chunks = new List<string>();
        int currentStart = 0;

        while (currentStart < text.Length)
        {
            int currentEnd = Math.Min(currentStart + maxChunkSize, text.Length);

            if (currentEnd == text.Length)
            {
                chunks.Add(text.Substring(currentStart));
                break;
            }

            int lastPeriod = text.LastIndexOf('。', currentEnd - 1, currentEnd - currentStart);
            int splitPoint = lastPeriod > currentStart + maxChunkSize / 2
                ? lastPeriod + 1
                : currentEnd;

            chunks.Add(text.Substring(currentStart, splitPoint - currentStart));
            currentStart = splitPoint;
        }

        return chunks;
    }

    private static string ComputeHash(string text)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private static double CalculateStdDev(IEnumerable<double> values, double? mean = null)
    {
        var valueList = values.ToList();
        if (valueList.Count == 0) return 0;

        double avg = mean ?? valueList.Average();
        double sumOfSquares = valueList.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumOfSquares / valueList.Count);
    }

    private void CalculateQualityMetrics(List<string> chunks, ChunkQualityMetrics metrics)
    {
        if (chunks.Count == 0) return;

        var sizes = chunks.Select(c => c.Length).ToList();
        metrics.TotalChunks = chunks.Count;
        metrics.AverageChunkSize = sizes.Average();
        metrics.MinChunkSize = sizes.Min();
        metrics.MaxChunkSize = sizes.Max();
        metrics.StdDevChunkSize = CalculateStdDev(sizes.Select(s => (double)s));

        // 统计碎片块
        foreach (var chunk in chunks)
        {
            var sentenceCount = Regex.Matches(chunk, @"[。！？.!?]").Count;
            if (sentenceCount == 1)
                metrics.SingleSentenceChunks++;
            else if (sentenceCount == 2)
                metrics.TwoSentenceChunks++;
        }
    }

    #endregion
}

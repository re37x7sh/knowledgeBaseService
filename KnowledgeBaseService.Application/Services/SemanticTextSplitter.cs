using System.Text;
using System.Text.RegularExpressions;
using KnowledgeBaseService.Application.DTOs;
using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Application.Options;
using Microsoft.Extensions.Logging;

namespace KnowledgeBaseService.Application.Services;

/// <summary>
/// 语义文本分割器实现
/// 基于句子边界和语义相似度进行智能分块，保证每个块的语义完整性
/// </summary>
public partial class SemanticTextSplitter : ISemanticTextSplitter
{
    private readonly IEmbeddingClient _embeddingClient;
    private readonly ILogger<SemanticTextSplitter> _logger;

    // 默认参数
    private const double DefaultSimilarityThreshold = 0.65;
    private const int DefaultMaxChunkSize = 1500;
    private const int MinSentencesPerChunk = 2;

    public SemanticTextSplitter(
        IEmbeddingClient embeddingClient,
        ILogger<SemanticTextSplitter> logger)
    {
        _embeddingClient = embeddingClient;
        _logger = logger;
    }

    /// <summary>
    /// 异步分割文本（基于语义边界）
    /// </summary>
    public async Task<List<string>> SplitAsync(
        string text,
        double similarityThreshold = DefaultSimilarityThreshold,
        int maxChunkSize = DefaultMaxChunkSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        if (maxChunkSize <= 0)
            throw new ArgumentException("Max chunk size must be greater than 0", nameof(maxChunkSize));

        if (similarityThreshold <= 0 || similarityThreshold >= 1)
            throw new ArgumentException("Similarity threshold must be between 0 and 1", nameof(similarityThreshold));

        _logger.LogInformation("开始语义分块: 阈值={Threshold}, 最大块大小={MaxSize}",
            similarityThreshold, maxChunkSize);

        // 1. 分句
        var sentences = SplitIntoSentences(text);
        _logger.LogDebug("文本已分割为 {Count} 个句子", sentences.Count);

        if (sentences.Count <= MinSentencesPerChunk)
        {
            _logger.LogInformation("句子数量过少，直接返回原文");
            return new List<string> { text.Trim() };
        }

        // 2. 批量生成句子 embedding
        float[][] embeddings;
        try
        {
            var embeddingResults = await _embeddingClient.GetEmbeddingsAsync(sentences, cancellationToken);
            embeddings = embeddingResults.Select(r => r.Vector ?? Array.Empty<float>()).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成句子 embedding 失败，回退到字符分块");
            return FallbackSplit(text, maxChunkSize);
        }

        if (embeddings.Length != sentences.Count || embeddings.Any(e => e.Length == 0))
        {
            _logger.LogWarning("部分 embedding 生成失败，回退到字符分块");
            return FallbackSplit(text, maxChunkSize);
        }

        // 3. 基于语义相似度合并句子成块
        var chunks = MergeSentencesIntoChunks(sentences, embeddings, similarityThreshold, maxChunkSize);

        _logger.LogInformation("语义分块完成: 原文 {Length} 字符 -> {Count} 个块",
            text.Length, chunks.Count);

        return chunks;
    }

    /// <summary>
    /// 异步分割文本（带质量指标）
    /// </summary>
    public async Task<(List<string> Chunks, ChunkQualityMetrics Metrics)> SplitWithMetricsAsync(
        string text,
        SemanticChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var opts = options ?? new SemanticChunkingOptions();

        if (string.IsNullOrWhiteSpace(text))
            return (new List<string>(), new ChunkQualityMetrics());

        _logger.LogInformation("开始语义分块（带指标）: 阈值={Threshold}, 最大块大小={MaxSize}",
            opts.SimilarityThreshold, opts.MaxChunkSize);

        // 1. 分句
        var sentences = SplitIntoSentences(text);
        var totalSentences = sentences.Count;

        _logger.LogDebug("文本已分割为 {Count} 个句子", totalSentences);

        if (totalSentences <= MinSentencesPerChunk)
        {
            return CreateSingleChunkResult(text, startTime);
        }

        // 2. 批量生成句子 embedding
        var embeddingStartTime = DateTime.UtcNow;
        float[][] embeddings;
        try
        {
            var embeddingResults = await _embeddingClient.GetEmbeddingsAsync(sentences, cancellationToken);
            embeddings = embeddingResults.Select(r => r.Vector ?? Array.Empty<float>()).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成句子 embedding 失败，回退到字符分块");
            return FallbackSplitWithMetrics(text, opts.MaxChunkSize, startTime, totalSentences);
        }

        var embeddingTimeMs = (long)(DateTime.UtcNow - embeddingStartTime).TotalMilliseconds;

        if (embeddings.Length != totalSentences || embeddings.Any(e => e.Length == 0))
        {
            _logger.LogWarning("部分 embedding 生成失败，回退到字符分块");
            return FallbackSplitWithMetrics(text, opts.MaxChunkSize, startTime, totalSentences);
        }

        // 3. 基于语义相似度合并句子成块（带指标）
        var mergeStartTime = DateTime.UtcNow;
        var (chunks, splitSimilarities, chunkSentenceCounts) =
            MergeSentencesIntoChunksWithMetrics(sentences, embeddings, opts);
        var mergingTimeMs = (long)(DateTime.UtcNow - mergeStartTime).TotalMilliseconds;

        // 4. 计算质量指标
        var metrics = CalculateMetrics(
            chunks, splitSimilarities, chunkSentenceCounts,
            totalSentences, embeddingTimeMs, mergingTimeMs, startTime);

        _logger.LogInformation("语义分块完成: 原文 {Length} 字符 -> {Count} 个块",
            text.Length, chunks.Count);

        return (chunks, metrics);
    }

    /// <summary>
    /// 创建单块结果（文本过短时）
    /// </summary>
    private static (List<string> Chunks, ChunkQualityMetrics Metrics) CreateSingleChunkResult(
        string text, DateTime startTime)
    {
        var chunks = new List<string> { text.Trim() };
        var metrics = new ChunkQualityMetrics
        {
            TotalChunks = 1,
            AverageChunkSize = text.Length,
            MinChunkSize = text.Length,
            MaxChunkSize = text.Length,
            StdDevChunkSize = 0,
            TotalSentences = 1,
            SingleSentenceChunks = 1,
            TotalProcessingTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
        };
        return (chunks, metrics);
    }

    /// <summary>
    /// 回退方案（带指标）
    /// </summary>
    private static (List<string> Chunks, ChunkQualityMetrics Metrics) FallbackSplitWithMetrics(
        string text, int maxChunkSize, DateTime startTime, int totalSentences)
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

        var sizes = chunks.Select(c => (double)c.Length).ToList();
        var metrics = new ChunkQualityMetrics
        {
            TotalChunks = chunks.Count,
            AverageChunkSize = chunks.Average(c => c.Length),
            MinChunkSize = chunks.Min(c => c.Length),
            MaxChunkSize = chunks.Max(c => c.Length),
            StdDevChunkSize = CalculateStdDev(sizes),
            TotalSentences = totalSentences,
            TotalProcessingTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
        };

        return (chunks, metrics);
    }

    /// <summary>
    /// 基于语义相似度合并句子成块（带指标收集）
    /// </summary>
    private static (List<string> Chunks, List<double> SplitSimilarities, List<int> SentenceCounts)
        MergeSentencesIntoChunksWithMetrics(
            List<string> sentences,
            float[][] embeddings,
            SemanticChunkingOptions opts)
    {
        var chunks = new List<string>();
        var splitSimilarities = new List<double>();
        var chunkSentenceCounts = new List<int>();

        var currentChunkSentences = new List<string> { sentences[0] };
        int currentChunkSize = sentences[0].Length;
        int currentSentenceCount = 1;

        for (int i = 1; i < sentences.Count; i++)
        {
            float similarity = CosineSimilarity(embeddings[i - 1], embeddings[i]);
            int newChunkSize = currentChunkSize + sentences[i].Length;

            bool shouldSplit = similarity < opts.SimilarityThreshold ||
                             newChunkSize > opts.MaxChunkSize ||
                             currentSentenceCount >= opts.MaxSentencesPerChunk;

            if (shouldSplit)
            {
                chunks.Add(string.Join("", currentChunkSentences));
                splitSimilarities.Add(similarity);
                chunkSentenceCounts.Add(currentSentenceCount);

                currentChunkSentences = new List<string> { sentences[i] };
                currentChunkSize = sentences[i].Length;
                currentSentenceCount = 1;
            }
            else
            {
                currentChunkSentences.Add(sentences[i]);
                currentChunkSize = newChunkSize;
                currentSentenceCount++;
            }
        }

        if (currentChunkSentences.Count > 0)
        {
            chunks.Add(string.Join("", currentChunkSentences));
            chunkSentenceCounts.Add(currentSentenceCount);
        }

        return (chunks, splitSimilarities, chunkSentenceCounts);
    }

    /// <summary>
    /// 计算质量指标
    /// </summary>
    private static ChunkQualityMetrics CalculateMetrics(
        List<string> chunks,
        List<double> splitSimilarities,
        List<int> chunkSentenceCounts,
        int totalSentences,
        long embeddingTimeMs,
        long mergingTimeMs,
        DateTime startTime)
    {
        var chunkSizes = chunks.Select(c => (double)c.Length).ToList();

        var metrics = new ChunkQualityMetrics
        {
            TotalChunks = chunks.Count,
            AverageChunkSize = chunks.Average(c => c.Length),
            MinChunkSize = chunks.Min(c => c.Length),
            MaxChunkSize = chunks.Max(c => c.Length),
            StdDevChunkSize = CalculateStdDev(chunkSizes),
            TotalSentences = totalSentences,
            TotalProcessingTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds,
            EmbeddingTimeMs = embeddingTimeMs,
            MergingTimeMs = mergingTimeMs,
            SingleSentenceChunks = chunkSentenceCounts.Count(c => c == 1),
            TwoSentenceChunks = chunkSentenceCounts.Count(c => c == 2)
        };

        if (splitSimilarities.Count > 0)
        {
            metrics.AverageSimilarity = splitSimilarities.Average();
            metrics.StdDevSimilarity = CalculateStdDev(splitSimilarities);
            metrics.LowSimilaritySplits = splitSimilarities.Count(s => s < 0.5);
        }

        return metrics;
    }

    /// <summary>
    /// 计算标准差
    /// </summary>
    private static double CalculateStdDev(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return 0;
        double avg = list.Average();
        double sumOfSquares = list.Sum(v => (v - avg) * (v - avg));
        return Math.Sqrt(sumOfSquares / list.Count);
    }

    /// <summary>
    /// 分句：保留原始分隔符
    /// 支持中英文标点符号
    /// </summary>
    private List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();

        // 正则表达式匹配句子（支持中英文）
        // 中文：。！？
        // 英文：. ! ?
        // 以及引号、括号等
        var pattern = new Regex(
            @"[^。！？.!?]+[。！？.!?]?(?=\s|$|[""”）]})]|$)",
            RegexOptions.Multiline | RegexOptions.Compiled);

        foreach (Match match in pattern.Matches(text))
        {
            var sentence = match.Value.Trim();
            if (sentence.Length > 0)
            {
                sentences.Add(sentence);
            }
        }

        // 如果正则分割失败，使用简单分割
        if (sentences.Count == 0 && text.Length > 0)
        {
            return SimpleSentenceSplit(text);
        }

        return sentences;
    }

    /// <summary>
    /// 简单分句（回退方案）
    /// </summary>
    private List<string> SimpleSentenceSplit(string text)
    {
        var sentences = new List<string>();
        var separators = new[] { "。", "！", "？", ".", "!", "?" };

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (separators.Any(s => s[0] == c))
            {
                var sentence = text.Substring(start, i - start + 1).Trim();
                if (sentence.Length > 0)
                {
                    sentences.Add(sentence);
                }
                start = i + 1;
            }
        }

        // 添加剩余部分
        if (start < text.Length)
        {
            var remaining = text.Substring(start).Trim();
            if (remaining.Length > 0)
            {
                sentences.Add(remaining);
            }
        }

        return sentences.Count > 0 ? sentences : new List<string> { text };
    }

    /// <summary>
    /// 基于语义相似度合并句子成块
    /// </summary>
    private List<string> MergeSentencesIntoChunks(
        List<string> sentences,
        float[][] embeddings,
        double similarityThreshold,
        int maxChunkSize)
    {
        var chunks = new List<string>();
        var currentChunkSentences = new List<string> { sentences[0] };
        int currentChunkSize = sentences[0].Length;

        for (int i = 1; i < sentences.Count; i++)
        {
            // 计算当前句子与前一个句子的语义相似度
            float similarity = CosineSimilarity(embeddings[i - 1], embeddings[i]);

            // 计算加入当前句子后的块大小
            int newChunkSize = currentChunkSize + sentences[i].Length;

            // 判断是否需要开始新块：
            // 1. 相似度低于阈值（语义变化大）
            // 2. 块大小超限
            bool shouldSplit = similarity < similarityThreshold ||
                             newChunkSize > maxChunkSize ||
                             currentChunkSentences.Count >= 20; // 防止单个块过长

            if (shouldSplit)
            {
                // 结束当前块
                chunks.Add(string.Join("", currentChunkSentences));

                // 开始新块
                currentChunkSentences = new List<string> { sentences[i] };
                currentChunkSize = sentences[i].Length;
            }
            else
            {
                // 继续合并到当前块
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

    /// <summary>
    /// 计算余弦相似度
    /// </summary>
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

    /// <summary>
    /// 回退方案：简单的字符分割
    /// </summary>
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

            // 尝试在句号处分割
            int lastPeriod = text.LastIndexOf('。', currentEnd - 1, currentEnd - currentStart);
            int splitPoint = lastPeriod > currentStart + maxChunkSize / 2
                ? lastPeriod + 1
                : currentEnd;

            chunks.Add(text.Substring(currentStart, splitPoint - currentStart));
            currentStart = splitPoint;
        }

        return chunks;
    }
}

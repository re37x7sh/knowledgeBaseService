using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using KnowledgeBaseService.Application.DTOs;
using KnowledgeBaseService.Core.Constants;
using KnowledgeBaseService.Core.Entities;
using KnowledgeBaseService.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KnowledgeBaseService.Application.Services;

/// <summary>
/// RAG 服务实现
/// 核心 RAG 流程：向量化 → 搜索 → 构建提示词 → LLM 生成
/// </summary>
public class RAGService : IRAGService
{
    private readonly IEmbeddingClient _embeddingClient;
    private readonly ILLMChatClient _chatClient;
    private readonly IQdrantHttpClient _qdrantClient;
    private readonly IDocumentRepository _documentRepository;
    private readonly ITextSplitter _textSplitter;
    private readonly ISemanticTextSplitter? _semanticTextSplitter;
    private readonly IHybridSearchService? _hybridSearchService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RAGService> _logger;
    private const string CollectionName = QdrantConstants.DefaultCollectionName;

    // 配置选项
    private readonly bool _useSemanticChunking;
    private readonly bool _useHybridSearch;
    private readonly double _semanticSimilarityThreshold;
    private readonly int _semanticMaxChunkSize;
    private readonly float _defaultVectorWeight;
    private readonly float _defaultBm25Weight;

    public RAGService(
        IEmbeddingClient embeddingClient,
        ILLMChatClient chatClient,
        IQdrantHttpClient qdrantClient,
        IDocumentRepository documentRepository,
        ITextSplitter textSplitter,
        ISemanticTextSplitter? semanticTextSplitter,
        IHybridSearchService? hybridSearchService,
        IConfiguration configuration,
        ILogger<RAGService> logger)
    {
        _embeddingClient = embeddingClient;
        _chatClient = chatClient;
        _qdrantClient = qdrantClient;
        _documentRepository = documentRepository;
        _textSplitter = textSplitter;
        _semanticTextSplitter = semanticTextSplitter;
        _hybridSearchService = hybridSearchService;
        _configuration = configuration;
        _logger = logger;

        // 读取配置
        _useSemanticChunking = configuration.GetValue<bool>("RAG:UseSemanticChunking", defaultValue: false);
        _useHybridSearch = configuration.GetValue<bool>("RAG:UseHybridSearch", defaultValue: false);
        _semanticSimilarityThreshold = configuration.GetValue<double>("RAG:SemanticChunking:SimilarityThreshold", defaultValue: 0.65);
        _semanticMaxChunkSize = configuration.GetValue<int>("RAG:SemanticChunking:MaxChunkSize", defaultValue: 1500);
        _defaultVectorWeight = configuration.GetValue<float>("RAG:HybridSearch:VectorWeight", defaultValue: 0.7f);
        _defaultBm25Weight = configuration.GetValue<float>("RAG:HybridSearch:Bm25Weight", defaultValue: 0.3f);

        _logger.LogInformation("RAG服务初始化: 语义分块={UseSemanticChunking}, 混合检索={UseHybridSearch}",
            _useSemanticChunking, _useHybridSearch);
    }

    /// <summary>
    /// 执行 RAG 查询（4步流程）
    /// </summary>
    public async Task<RAGQueryResponse> QueryAsync(RAGQueryRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(request.Question))
                throw new ArgumentException("问题不能为空");

            _logger.LogInformation("开始执行 RAG 查询: {Question}", request.Question);
            _logger.LogInformation("步骤1：生成问题向量...");
            _logger.LogInformation("步骤2：在 Qdrant 中检索相关分块...");

            var preparation = await PrepareRetrievalContextAsync(request, isStreaming: false, cancellationToken);

            _logger.LogInformation("步骤3：构建提示词...");

            var systemPrompt = request.EnableHybridMode
                ? BuildHybridModeSystemPrompt()
                : BuildStrictModeSystemPrompt();

            var hasContext = preparation.ResultsToUse.Count > 0;
            var userMessage = hasContext
                ? preparation.ContextText + $"\n用户问题: {request.Question}"
                : request.Question;

            var messages = new List<ChatMessage>
            {
                new(MessageRole.System, systemPrompt),
                new(MessageRole.User, userMessage)
            };

            _logger.LogInformation("步骤4：调用 LLM 生成答案...");
            var answer = await _chatClient.GetCompletionAsync(
                messages,
                temperature: request.Temperature,
                maxTokens: request.MaxTokens,
                cancellationToken: cancellationToken);

            stopwatch.Stop();

            var response = new RAGQueryResponse
            {
                Question = request.Question,
                Answer = answer,
                Sources = preparation.Sources,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                TokensUsed = preparation.EmbeddingResult.Tokens + (answer.Length / 4)
            };

            _logger.LogInformation("RAG 查询完成，耗时 {Ms} 毫秒", stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG 查询失败");
            throw;
        }
    }

    /// <summary>
    /// 执行 RAG 流式查询
    /// </summary>
    public async IAsyncEnumerable<string> QueryStreamAsync(RAGQueryRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            throw new ArgumentException("问题不能为空");

        _logger.LogInformation("开始执行流式 RAG 查询: {Question}", request.Question);

        var preparation = await PrepareRetrievalContextAsync(request, isStreaming: true, cancellationToken);

        var systemPrompt = request.EnableHybridMode
            ? BuildHybridModeSystemPrompt()
            : BuildStrictModeSystemPrompt();

        var hasContext = preparation.ResultsToUse.Count > 0;
        var userMessage = hasContext
            ? preparation.ContextText + $"\n用户问题: {request.Question}"
            : request.Question;

        var messages = new List<ChatMessage>
        {
            new(MessageRole.System, systemPrompt),
            new(MessageRole.User, userMessage)
        };

        if (preparation.Sources.Count > 0)
        {
            var jsonOptions = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            };
            var sourcesJson = System.Text.Json.JsonSerializer.Serialize(new { sources = preparation.Sources }, jsonOptions);
            yield return $"[SOURCES]{sourcesJson}[/SOURCES]";
        }

        await foreach (var chunk in _chatClient.GetCompletionStreamAsync(
            messages,
            temperature: request.Temperature,
            maxTokens: request.MaxTokens,
            cancellationToken: cancellationToken))
        {
            yield return chunk;
        }
    }

    // 将向量化、检索与上下文构建统一封装，供同步与流式查询重用。
    private async Task<RetrievalPreparationResult> PrepareRetrievalContextAsync(
        RAGQueryRequest request,
        bool isStreaming,
        CancellationToken cancellationToken)
    {
        // 获取问题向量（无论哪种检索方式都需要）
        var embeddingResult = await _embeddingClient.GetEmbeddingAsync(request.Question, cancellationToken);
        var questionVector = embeddingResult.Vector
            ?? throw new InvalidOperationException("无法获取问题向量");

        // 判断是否使用混合检索
        bool useHybridSearch = request.EnableHybridSearch && _useHybridSearch && _hybridSearchService != null;

        List<(ulong PointId, float Score, Dictionary<string, object> Payload)> searchResults;

        if (useHybridSearch)
        {
            // 混合检索
            _logger.LogInformation("使用混合检索（向量+BM25）");

            var hybridResults = await _hybridSearchService!.SearchAsync(
                CollectionName,
                request.Question,
                questionVector,
                topK: request.TopK * 2, // 获取更多结果用于过滤
                vectorWeight: request.VectorWeight > 0 ? request.VectorWeight : _defaultVectorWeight,
                bm25Weight: request.Bm25Weight > 0 ? request.Bm25Weight : _defaultBm25Weight,
                documentIds: request.DocumentIds,
                cancellationToken: cancellationToken);

            // 转换为标准格式
            searchResults = hybridResults
                .Select(r => (r.PointId, r.Score, r.Payload))
                .ToList();

            _logger.LogInformation("混合检索完成: 返回 {Count} 条结果", searchResults.Count);
        }
        else
        {
            // 纯向量检索
            float scoreThreshold;
            int effectiveTopK;

            if (request.DocumentIds != null && request.DocumentIds.Count > 0)
            {
                if (isStreaming)
                {
                    _logger.LogInformation("流式检索：启用文档过滤条件");
                }

                _logger.LogInformation("在 {DocCount} 个指定文档中检索: {DocumentIds}",
                    request.DocumentIds.Count, string.Join(", ", request.DocumentIds));

                scoreThreshold = 0.15f;
                effectiveTopK = Math.Min(request.TopK * 4, QdrantConstants.MaxTopK);

                _logger.LogInformation("文档过滤模式：使用阈值 {Threshold} 与 topK {TopK}",
                    scoreThreshold, effectiveTopK);
            }
            else
            {
                _logger.LogInformation("全局检索：在全部文档中搜索相关内容");
                scoreThreshold = 0.3f;
                effectiveTopK = Math.Min(request.TopK, QdrantConstants.MaxTopK);
            }

            searchResults = await _qdrantClient.SearchAsync(
                CollectionName,
                questionVector,
                topK: effectiveTopK,
                scoreThreshold: scoreThreshold,
                documentIds: request.DocumentIds,
                cancellationToken: cancellationToken);
        }

        if (isStreaming)
        {
            _logger.LogInformation("流式检索结果数量: {Count}", searchResults.Count);
        }
        else
        {
            _logger.LogInformation("检索到 {Count} 条相似分块", searchResults.Count);
        }

        if (searchResults.Count > 0)
        {
            foreach (var (pointId, score, payload) in searchResults.Take(10))
            {
                var title = payload.TryGetValue("title", out var titleObj) && titleObj is string t ? t : "未知";

                if (isStreaming)
                {
                    _logger.LogInformation("  - 文档: {Title}，相关度: {Score:F4}", title, score);
                }
                else
                {
                    _logger.LogInformation("  - 文档: {Title}，相关度: {Score:F4}，PointId: {PointId}", title, score, pointId);
                }
            }
        }
        else
        {
            if (isStreaming)
            {
                _logger.LogWarning("流式查询未在 Qdrant 中找到相关内容");
            }
            else
            {
                _logger.LogWarning("本次查询未在 Qdrant 中找到相关内容");
            }
        }

        var highRelevantResults = searchResults.Where(x => x.Score > 0.6f).ToList();
        var lowRelevantResults = searchResults.Where(x => x.Score <= 0.6f).ToList();
        var resultsToUse = highRelevantResults.Count > 0 ? highRelevantResults : lowRelevantResults;

        if (isStreaming)
        {
            _logger.LogInformation("流式检索：高相关分块 {HighCount} 个，低相关分块 {LowCount} 个",
                highRelevantResults.Count, lowRelevantResults.Count);
        }
        else
        {
            _logger.LogInformation("将使用 {HighCount} 个高相关分块和 {LowCount} 个低相关分块，总计 {UseCount} 个",
                highRelevantResults.Count, lowRelevantResults.Count, resultsToUse.Count);
        }

        var sources = new List<SourceReference>();
        var contextBuilder = new StringBuilder();

        if (resultsToUse.Count > 0)
        {
            contextBuilder.AppendLine("基于以下相关文档，请回答用户的问题:\n");
        }

        foreach (var (_, score, payload) in resultsToUse)
        {
            string content = string.Empty;
            string title = string.Empty;
            string documentId = string.Empty;
            string sourceUrl = string.Empty;

            if (payload.TryGetValue("content", out var contentObj) && contentObj is string chunkContent)
            {
                content = chunkContent;

                if (isStreaming)
                {
                    var contentPreview = content.Length > 200 ? content.Substring(0, 200) + "..." : content;
                    _logger.LogInformation("检索到的内容片段（前 200 字符）: {Content}", contentPreview);
                }
            }
            else if (isStreaming)
            {
                _logger.LogWarning("检索结果缺少内容字段");
            }

            if (payload.TryGetValue("title", out var titleObj) && titleObj is string docTitle)
            {
                title = docTitle;
            }

            if (payload.TryGetValue("document_id", out var docIdObj) && docIdObj is string docId)
            {
                documentId = docId;
            }

            if (string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(documentId))
            {
                var document = await _documentRepository.GetByIdAsync(documentId);
                if (document != null)
                {
                    title = document.Title;
                    sourceUrl = document.SourceUrl ?? string.Empty;
                    content = document.Content.Length > 200
                        ? document.Content.Substring(0, 200) + "..."
                        : document.Content;
                }
            }

            if (string.IsNullOrEmpty(content))
                continue;

            contextBuilder.AppendLine($"【{title}】(相关度: {score:P1})");
            contextBuilder.AppendLine(content);
            contextBuilder.AppendLine();

            var source = new SourceReference
            {
                DocumentId = documentId,
                Title = title,
                Score = score,
                Snippet = content.Length > 100 ? content.Substring(0, 100) + "..." : content,
                SourceUrl = sourceUrl
            };

            if (content.Contains("[图片路径:"))
            {
                source.FileType = "image";

                var pathMatch = System.Text.RegularExpressions.Regex.Match(
                    content,
                    @"\[图片路径:\s*([^\]]+)\]");

                if (pathMatch.Success)
                {
                    var imagePath = pathMatch.Groups[1].Value.Trim();
                    var fullPath = Path.Combine("wwwroot", imagePath.TrimStart('/'));

                    try
                    {
                        if (File.Exists(fullPath))
                        {
                            var imageBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
                            source.ImageBase64 = Convert.ToBase64String(imageBytes);

                            var textWithoutMetadata = System.Text.RegularExpressions.Regex.Replace(
                                content,
                                @"\[图片文件:.*?\]\s*\[图片路径:.*?\]\s*",
                                "");

                            source.MatchHint = textWithoutMetadata.Length > 150
                                ? textWithoutMetadata.Substring(0, 150) + "..."
                                : textWithoutMetadata;

                            if (isStreaming)
                            {
                                _logger.LogInformation("已在流式查询中加载图片 Base64，大小: {Size} KB", imageBytes.Length / 1024);
                            }
                            else
                            {
                                _logger.LogInformation("已加载图片 Base64，大小: {Size} KB", imageBytes.Length / 1024);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "无法加载图片文件: {ImagePath}", imagePath);
                    }
                }
            }

            sources.Add(source);
        }

        var contextText = resultsToUse.Count > 0 ? contextBuilder.ToString() : string.Empty;

        return new RetrievalPreparationResult(
            embeddingResult,
            resultsToUse,
            sources,
            contextText);
    }

    private sealed class RetrievalPreparationResult
    {
        public RetrievalPreparationResult(
            EmbeddingResult embeddingResult,
            List<(ulong PointId, float Score, Dictionary<string, object> Payload)> resultsToUse,
            List<SourceReference> sources,
            string contextText)
        {
            EmbeddingResult = embeddingResult;
            ResultsToUse = resultsToUse;
            Sources = sources;
            ContextText = contextText;
        }

        public EmbeddingResult EmbeddingResult { get; }

        public List<(ulong PointId, float Score, Dictionary<string, object> Payload)> ResultsToUse { get; }

        public List<SourceReference> Sources { get; }

        public string ContextText { get; }
    }

    /// <summary>
    /// 索引文档（创建文档后调用）
    /// </summary>
    public async Task<bool> IndexDocumentAsync(string documentId, string content, Dictionary<string, object> metadata, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始索引文档: {DocumentId}", documentId);

            // 1. 分割文本（根据配置选择分块方式）
            List<string> chunks;

            if (_useSemanticChunking && _semanticTextSplitter != null)
            {
                _logger.LogInformation("使用语义分块");
                chunks = await _semanticTextSplitter.SplitAsync(
                    content,
                    similarityThreshold: _semanticSimilarityThreshold,
                    maxChunkSize: _semanticMaxChunkSize,
                    cancellationToken: cancellationToken);
            }
            else
            {
                _logger.LogInformation("使用字符分块");
                chunks = _textSplitter.Split(content);
            }

            _logger.LogInformation("文档被拆分为 {Count} 个分块", chunks.Count);

            if (chunks.Count == 0) return false;

            // 2. 逐个处理分块
            // 注意：这里可以优化为批量处理，但为了简单起见，先逐个处理
            // Qdrant 支持批量 Upsert，如果性能有瓶颈可以优化

            bool allSuccess = true;

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];

                // 向量化分块
                var embeddingResult = await _embeddingClient.GetEmbeddingAsync(chunk, cancellationToken);
                var vector = embeddingResult.Vector;

                if (vector == null)
                {
                    _logger.LogError("无法为文档 {DocumentId} 的第 {Index} 个分块生成向量", documentId, i);
                    allSuccess = false;
                    continue;
                }

                // 生成 Point ID
                // 使用 documentId + chunkIndex 组合生成唯一 ID
                var pointId = GeneratePointId(documentId, i);

                // 准备元数据（从传入的 metadata 复制，避免修改原始字典）
                var chunkMetadata = new Dictionary<string, object>(metadata);

                // 设置或覆盖分块相关的元数据
                chunkMetadata["document_id"] = documentId;
                chunkMetadata["chunk_index"] = i;
                chunkMetadata["content"] = chunk; // 存储分块内容用于检索
                chunkMetadata["total_chunks"] = chunks.Count;

                // 上传到 Qdrant
                var success = await _qdrantClient.UpsertPointAsync(
                    CollectionName,
                    pointId,
                    vector,
                    chunkMetadata,
                    cancellationToken);

                if (!success) allSuccess = false;
            }

            if (allSuccess)
            {
                // 注意：分块存储模式下，每个文档有多个 point，不再单独存储 PointId
                // 检索时通过 metadata 中的 document_id 过滤即可
                _logger.LogInformation("完成索引: 文档 {DocumentId}，分块数量 {Count}", documentId, chunks.Count);
            }

            return allSuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "索引文档失败: {DocumentId}", documentId);
            throw;
        }
    }

    /// <summary>
    /// 根据文档ID和分块索引生成点ID
    /// </summary>
    private static ulong GeneratePointId(string documentId, int chunkIndex = 0)
    {
        var input = $"{documentId}_{chunkIndex}";
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToUInt64(hash, 0);
    }

    /// <summary>
    /// 构建严格模式的系统提示词
    /// 仅基于提供的知识库文档进行回答
    /// </summary>
    private static string BuildStrictModeSystemPrompt()
    {
        return "你是一个知识库助手。请严格根据提供的文档内容回答用户的问题。" +
               "如果文档中没有相关信息，请明确说明无法回答。" +
               "回答应该简洁、准确且基于文档内容。";
    }

    /// <summary>
    /// 构建混合模式的系统提示词
    /// 首先基于知识库回答，若知识库信息不足则自动补充通用知识
    /// </summary>
    private static string BuildHybridModeSystemPrompt()
    {
        return "你是一个知识库助手。请按照以下步骤处理用户的问题：\n" +
               "1. 首先，严格基于提供的文档内容回答用户的问题\n" +
               "2. 如果你认为仅基于这些文档无法充分回答问题，请在回答中做出补充回答\n" +
               "3. 在回答中清晰区分哪些内容来自知识库，哪些是补充的通用知识（可使用「根据文档：」或「补充说明：」的表述区分）\n" +
               "4. 确保回答准确、全面且易于理解";
    }
}

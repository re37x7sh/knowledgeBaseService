using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using KnowledgeBaseService.Application.DTOs;
using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Qdrant;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Text;

namespace KnowledgeBaseService.Infrastructure.SemanticKernel;

/// <summary>
/// 基于 Semantic Kernel 的 RAG 服务实现
/// 使用 SK 的 ISemanticTextMemory 进行向量存储和检索
/// 支持语义分块和混合检索（BM25+Vector）
/// </summary>
public class SemanticKernelRAGService : IRAGService
{
    private readonly Kernel _kernel;
    private readonly ISemanticTextMemory _memory;
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly IChatCompletionService _chatService;
    private readonly IDocumentRepository _documentRepository;
    private readonly ISemanticTextSplitter? _semanticTextSplitter;
    private readonly IQdrantHttpClient? _qdrantClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SemanticKernelRAGService> _logger;

    private const string CollectionName = "knowledge_base";

    // 配置选项
    private readonly bool _useSemanticChunking;
    private readonly bool _useHybridSearch;
    private readonly double _semanticSimilarityThreshold;
    private readonly int _semanticMaxChunkSize;
    private readonly float _defaultVectorWeight;
    private readonly float _defaultBm25Weight;

    public SemanticKernelRAGService(
        Kernel kernel,
        ISemanticTextMemory memory,
        ITextEmbeddingGenerationService embeddingService,
        IChatCompletionService chatService,
        IDocumentRepository documentRepository,
        ISemanticTextSplitter? semanticTextSplitter,
        IQdrantHttpClient? qdrantClient,
        IConfiguration configuration,
        ILogger<SemanticKernelRAGService> logger)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _semanticTextSplitter = semanticTextSplitter;
        _qdrantClient = qdrantClient;
        _configuration = configuration;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 读取配置
        _useSemanticChunking = configuration.GetValue<bool>("RAG:UseSemanticChunking", defaultValue: false);
        _useHybridSearch = configuration.GetValue<bool>("RAG:UseHybridSearch", defaultValue: false);
        _semanticSimilarityThreshold = configuration.GetValue<double>("RAG:SemanticChunking:SimilarityThreshold", defaultValue: 0.65);
        _semanticMaxChunkSize = configuration.GetValue<int>("RAG:SemanticChunking:MaxChunkSize", defaultValue: 1500);
        _defaultVectorWeight = configuration.GetValue<float>("RAG:HybridSearch:VectorWeight", defaultValue: 0.7f);
        _defaultBm25Weight = configuration.GetValue<float>("RAG:HybridSearch:Bm25Weight", defaultValue: 0.3f);

        _logger.LogInformation("SK RAG服务初始化: 语义分块={UseSemanticChunking}, 混合检索={UseHybridSearch}",
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

            _logger.LogInformation("SK RAG: 开始执行查询: {Question}", request.Question);

            // 步骤1-2: 使用 SK Memory 进行语义搜索（自动处理 embedding + search）
            _logger.LogInformation("SK RAG 步骤1-2: 使用 Semantic Memory 检索相关内容...");
            
            var searchResults = await SearchMemoryAsync(request, cancellationToken);

            _logger.LogInformation("SK RAG: 检索到 {Count} 条相关内容", searchResults.Count);

            // 步骤3: 构建提示词
            _logger.LogInformation("SK RAG 步骤3: 构建提示词...");

            var (contextText, sources) = BuildContext(searchResults);

            var systemPrompt = request.EnableHybridMode
                ? BuildHybridModeSystemPrompt()
                : BuildStrictModeSystemPrompt();

            var hasContext = searchResults.Count > 0;
            var userMessage = hasContext
                ? contextText + $"\n用户问题: {request.Question}"
                : request.Question;

            // 步骤4: 使用 SK ChatCompletion 生成答案
            _logger.LogInformation("SK RAG 步骤4: 调用 LLM 生成答案...");

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(systemPrompt);
            chatHistory.AddUserMessage(userMessage);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "temperature", request.Temperature },
                    { "max_tokens", request.MaxTokens }
                }
            };

            var result = await _chatService.GetChatMessageContentsAsync(
                chatHistory,
                executionSettings,
                _kernel,
                cancellationToken);

            var answer = result.FirstOrDefault()?.Content ?? string.Empty;

            stopwatch.Stop();

            var response = new RAGQueryResponse
            {
                Question = request.Question,
                Answer = answer,
                Sources = sources,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                TokensUsed = answer.Length / 4 // 估算
            };

            _logger.LogInformation("SK RAG: 查询完成，耗时 {Ms} 毫秒", stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SK RAG: 查询失败");
            throw;
        }
    }

    /// <summary>
    /// 执行 RAG 流式查询
    /// </summary>
    public async IAsyncEnumerable<string> QueryStreamAsync(
        RAGQueryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            throw new ArgumentException("问题不能为空");

        _logger.LogInformation("SK RAG Stream: 开始流式查询: {Question}", request.Question);

        // 步骤1-2: 检索
        var searchResults = await SearchMemoryAsync(request, cancellationToken);
        _logger.LogInformation("SK RAG Stream: 检索到 {Count} 条相关内容", searchResults.Count);

        // 步骤3: 构建上下文
        var (contextText, sources) = BuildContext(searchResults);

        var systemPrompt = request.EnableHybridMode
            ? BuildHybridModeSystemPrompt()
            : BuildStrictModeSystemPrompt();

        var hasContext = searchResults.Count > 0;
        var userMessage = hasContext
            ? contextText + $"\n用户问题: {request.Question}"
            : request.Question;

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage(userMessage);

        // 先返回 sources 信息
        if (sources.Count > 0)
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var sourcesJson = JsonSerializer.Serialize(new { sources }, jsonOptions);
            yield return $"[SOURCES]{sourcesJson}[/SOURCES]";
        }

        var executionSettings = new PromptExecutionSettings
        {
            ExtensionData = new Dictionary<string, object>
            {
                { "temperature", request.Temperature },
                { "max_tokens", request.MaxTokens }
            }
        };

        // 步骤4: 流式生成
        await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(
            chatHistory,
            executionSettings,
            _kernel,
            cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                yield return chunk.Content;
            }
        }

        _logger.LogInformation("SK RAG Stream: 流式查询完成");
    }

    /// <summary>
    /// 索引文档（创建文档后调用）
    /// 支持语义分块
    /// </summary>
    public async Task<bool> IndexDocumentAsync(
        string documentId,
        string content,
        Dictionary<string, object> metadata,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("SK RAG: 开始索引文档: {DocumentId}", documentId);

            // 分割文本（根据配置选择分块方式）
            List<string> chunks;

            if (_useSemanticChunking && _semanticTextSplitter != null)
            {
                _logger.LogInformation("SK RAG: 使用语义分块");
                chunks = await _semanticTextSplitter.SplitAsync(
                    content,
                    similarityThreshold: _semanticSimilarityThreshold,
                    maxChunkSize: _semanticMaxChunkSize,
                    cancellationToken: cancellationToken);
            }
            else
            {
                _logger.LogInformation("SK RAG: 使用 SK TextChunker 分块");
                var lines = TextChunker.SplitPlainTextLines(content, maxTokensPerLine: 200);
                var paragraphChunks = TextChunker.SplitPlainTextParagraphs(
                    lines,
                    maxTokensPerParagraph: 500,
                    overlapTokens: 50);
                chunks = paragraphChunks.ToList();
            }

            _logger.LogInformation("SK RAG: 文档被拆分为 {Count} 个分块", chunks.Count);

            if (chunks.Count == 0) return false;

            // 提取元数据
            var title = metadata.TryGetValue("title", out var titleObj) && titleObj is string t ? t : "未知";
            var category = metadata.TryGetValue("category", out var catObj) && catObj is string c ? c : "默认";

            // 逐个分块保存到 Memory
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunkId = $"{documentId}_chunk_{i}";
                var chunk = chunks[i];

                // 构建完整的元数据 JSON
                var additionalMetadata = JsonSerializer.Serialize(new
                {
                    document_id = documentId,
                    chunk_index = i,
                    total_chunks = chunks.Count,
                    title = title,
                    category = category,
                    content = chunk
                });

                await _memory.SaveInformationAsync(
                    collection: CollectionName,
                    id: chunkId,
                    text: chunk,
                    description: $"文档: {title}, 分块: {i + 1}/{chunks.Count}",
                    additionalMetadata: additionalMetadata,
                    cancellationToken: cancellationToken);
            }

            _logger.LogInformation("SK RAG: 完成索引: 文档 {DocumentId}，分块数量 {Count}", documentId, chunks.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SK RAG: 索引文档失败: {DocumentId}", documentId);
            throw;
        }
    }

    /// <summary>
    /// 使用 Semantic Memory 搜索相关内容
    /// 支持混合检索（向量+BM25）
    /// </summary>
    private async Task<List<MemoryQueryResult>> SearchMemoryAsync(
        RAGQueryRequest request,
        CancellationToken cancellationToken)
    {
        // 判断是否使用混合检索
        bool useHybridSearch = request.EnableHybridSearch && _useHybridSearch && _qdrantClient != null;

        if (useHybridSearch)
        {
            return await HybridSearchAsync(request, cancellationToken);
        }

        // 纯向量检索（使用 SK Memory）
        return await VectorSearchAsync(request, cancellationToken);
    }

    /// <summary>
    /// 纯向量检索（使用 SK Memory）
    /// </summary>
    private async Task<List<MemoryQueryResult>> VectorSearchAsync(
        RAGQueryRequest request,
        CancellationToken cancellationToken)
    {
        var results = new List<MemoryQueryResult>();

        // 确定搜索参数
        var minRelevanceScore = request.DocumentIds?.Count > 0 ? 0.15 : 0.3;
        var topK = request.DocumentIds?.Count > 0
            ? Math.Min(request.TopK * 4, 50)
            : Math.Min(request.TopK, 20);

        _logger.LogInformation("SK RAG: 使用向量检索, topK={TopK}, minRelevance={MinRelevance}",
            topK, minRelevanceScore);

        // 使用 SK Memory 搜索
        await foreach (var result in _memory.SearchAsync(
            collection: CollectionName,
            query: request.Question,
            limit: topK,
            minRelevanceScore: minRelevanceScore,
            withEmbeddings: false,
            cancellationToken: cancellationToken))
        {
            // 如果指定了文档过滤，检查是否匹配
            if (request.DocumentIds?.Count > 0)
            {
                var metadata = TryParseMetadata(result.Metadata.AdditionalMetadata);
                if (metadata != null &&
                    metadata.TryGetValue("document_id", out var docIdElement) &&
                    docIdElement.ValueKind == JsonValueKind.String)
                {
                    var docId = docIdElement.GetString();
                    if (docId == null || !request.DocumentIds.Contains(docId))
                        continue;
                }
            }

            results.Add(result);
        }

        _logger.LogInformation("SK RAG: 向量检索返回 {Count} 条结果", results.Count);
        return results;
    }

    /// <summary>
    /// 混合检索（向量+BM25）
    /// </summary>
    private async Task<List<MemoryQueryResult>> HybridSearchAsync(
        RAGQueryRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("SK RAG: 使用混合检索（向量+BM25）");

        // 并行执行向量检索和 BM25 检索
        var (vectorResults, bm25Results) = await Task.WhenAll(
            VectorSearchAsync(request, cancellationToken),
            Bm25SearchAsync(request, cancellationToken)
        ).ContinueWith(t => (t.Result[0], t.Result[1]), cancellationToken);

        _logger.LogInformation("SK RAG: 混合检索 - 向量返回 {VectorCount} 条, BM25返回 {Bm25Count} 条",
            vectorResults.Count, bm25Results.Count);

        // 使用 RRF 融合结果
        var fusedResults = ReciprocalRankFusion(
            vectorResults,
            bm25Results,
            request.VectorWeight > 0 ? request.VectorWeight : _defaultVectorWeight,
            request.Bm25Weight > 0 ? request.Bm25Weight : _defaultBm25Weight);

        _logger.LogInformation("SK RAG: 混合检索融合完成，返回 {Count} 条结果", fusedResults.Count);
        return fusedResults;
    }

    /// <summary>
    /// BM25 检索（使用 Qdrant）
    /// </summary>
    private async Task<List<MemoryQueryResult>> Bm25SearchAsync(
        RAGQueryRequest request,
        CancellationToken cancellationToken)
    {
        var results = new List<MemoryQueryResult>();

        if (_qdrantClient == null)
            return results;

        try
        {
            var topK = request.DocumentIds?.Count > 0
                ? Math.Min(request.TopK * 4, 10)
                : Math.Min(request.TopK, 10);

            var qdrantResults = await _qdrantClient.SearchByTextAsync(
                CollectionName,
                request.Question,
                topK: topK,
                documentIds: request.DocumentIds,
                cancellationToken: cancellationToken);

            // 转换为 MemoryQueryResult
            foreach (var (pointId, score, payload) in qdrantResults)
            {
                // 从 payload 提取信息
                var title = payload.TryGetValue("title", out var titleObj) && titleObj is string t
                    ? $"文档: {t}"
                    : "未知文档";

                var content = payload.TryGetValue("content", out var contentObj) && contentObj is string c
                    ? c
                    : string.Empty;

                // 文档过滤检查
                if (request.DocumentIds?.Count > 0)
                {
                    if (payload.TryGetValue("document_id", out var docIdObj) && docIdObj is string docId)
                    {
                        if (!request.DocumentIds.Contains(docId))
                            continue;
                    }
                }

                // 构造 MemoryQueryResult
                var metadata = new MemoryRecordMetadata(
                    isReference: false,
                    id: pointId.ToString(),
                    text: content,
                    description: title,
                    externalSourceName: CollectionName,
                    additionalMetadata: JsonSerializer.Serialize(payload));

                var memoryResult = new MemoryQueryResult(metadata, score, null);
                results.Add(memoryResult);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SK RAG: BM25 检索失败，将仅使用向量检索");
        }

        return results;
    }

    /// <summary>
    /// RRF 结果融合
    /// </summary>
    private List<MemoryQueryResult> ReciprocalRankFusion(
        List<MemoryQueryResult> vectorResults,
        List<MemoryQueryResult> bm25Results,
        float vectorWeight,
        float bm25Weight)
    {
        const int k = 60;

        // 存储融合后的结果: ID -> (Result, FusedScore)
        var fusedScores = new Dictionary<string, (MemoryQueryResult Result, float Score)>();

        // 处理向量检索结果
        for (int rank = 0; rank < vectorResults.Count; rank++)
        {
            var result = vectorResults[rank];
            var id = result.Metadata.Id ?? result.Metadata.Description.GetHashCode().ToString();

            if (!fusedScores.ContainsKey(id))
            {
                fusedScores[id] = (result, 0);
            }

            var rrfScore = vectorWeight / (k + rank + 1);
            fusedScores[id] = (fusedScores[id].Result, fusedScores[id].Score + rrfScore);
        }

        // 处理 BM25 检索结果
        for (int rank = 0; rank < bm25Results.Count; rank++)
        {
            var result = bm25Results[rank];
            var id = result.Metadata.Id ?? result.Metadata.Description.GetHashCode().ToString();

            if (!fusedScores.ContainsKey(id))
            {
                fusedScores[id] = (result, 0);
            }

            var rrfScore = bm25Weight / (k + rank + 1);
            fusedScores[id] = (fusedScores[id].Result, fusedScores[id].Score + rrfScore);
        }

        // 按 RRF 分数排序，返回融合后的结果
        return fusedScores.Values
            .OrderByDescending(x => x.Score)
            .Select(x =>
            {
                // 创建新的 MemoryQueryResult，使用融合后的分数
                var originalResult = x.Result;
                return new MemoryQueryResult(
                    originalResult.Metadata,
                    x.Score, // 使用融合后的分数
                    originalResult.Embedding);
            })
            .ToList();
    }

    /// <summary>
    /// 构建上下文和来源引用
    /// </summary>
    private (string ContextText, List<SourceReference> Sources) BuildContext(List<MemoryQueryResult> searchResults)
    {
        var sources = new List<SourceReference>();
        var contextBuilder = new StringBuilder();

        // 分离高相关和低相关结果
        var highRelevant = searchResults.Where(x => x.Relevance > 0.6).ToList();
        var lowRelevant = searchResults.Where(x => x.Relevance <= 0.6).ToList();
        var resultsToUse = highRelevant.Count > 0 ? highRelevant : lowRelevant;

        _logger.LogInformation("SK RAG: 高相关 {High} 个，低相关 {Low} 个，使用 {Use} 个",
            highRelevant.Count, lowRelevant.Count, resultsToUse.Count);

        if (resultsToUse.Count > 0)
        {
            contextBuilder.AppendLine("基于以下相关文档，请回答用户的问题:\n");
        }

        foreach (var result in resultsToUse)
        {
            var metadata = TryParseMetadata(result.Metadata.AdditionalMetadata);
            
            var title = result.Metadata.Description ?? "未知";
            var content = result.Metadata.Text;
            var documentId = string.Empty;
            var score = (float)result.Relevance;

            if (metadata != null)
            {
                if (metadata.TryGetValue("document_id", out var docIdElement) &&
                    docIdElement.ValueKind == JsonValueKind.String)
                {
                    documentId = docIdElement.GetString() ?? string.Empty;
                }
                if (metadata.TryGetValue("title", out var titleElement) &&
                    titleElement.ValueKind == JsonValueKind.String)
                {
                    title = titleElement.GetString() ?? title;
                }
            }

            contextBuilder.AppendLine($"【{title}】(相关度: {score:P1})");
            contextBuilder.AppendLine(content);
            contextBuilder.AppendLine();

            sources.Add(new SourceReference
            {
                DocumentId = documentId,
                Title = title,
                Score = score,
                Snippet = content.Length > 100 ? content.Substring(0, 100) + "..." : content
            });
        }

        return (contextBuilder.ToString(), sources);
    }

    /// <summary>
    /// 尝试解析元数据 JSON
    /// </summary>
    private static Dictionary<string, JsonElement>? TryParseMetadata(string? additionalMetadata)
    {
        if (string.IsNullOrEmpty(additionalMetadata))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(additionalMetadata);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 构建严格模式的系统提示词
    /// </summary>
    private static string BuildStrictModeSystemPrompt()
    {
        return "你是一个知识库助手。请严格根据提供的文档内容回答用户的问题。" +
               "如果文档中没有相关信息，请明确说明无法回答。" +
               "回答应该简洁、准确且基于文档内容。";
    }

    /// <summary>
    /// 构建混合模式的系统提示词
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

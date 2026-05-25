using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;

namespace KnowledgeBaseService.Infrastructure.SemanticKernel;

/// <summary>
/// OpenAI 兼容 Embedding 服务的 Semantic Kernel 实现
/// 支持豆包、OpenAI、DeepSeek、Qwen 等兼容 OpenAI API 格式的提供商
/// 实现 ITextEmbeddingGenerationService 接口
/// </summary>
public class OpenAICompatibleEmbeddingService : ITextEmbeddingGenerationService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _embeddingEndpoint;
    private readonly string _modelId;
    private readonly int _vectorDimension;
    private readonly string _provider;
    private readonly ILogger<OpenAICompatibleEmbeddingService> _logger;

    public OpenAICompatibleEmbeddingService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OpenAICompatibleEmbeddingService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // 统一从 LLM 配置节读取（向后兼容 DeepSeek 配置）
        _apiKey = configuration["LLM:ApiKey"] 
            ?? configuration["DeepSeek:ApiKey"] 
            ?? throw new InvalidOperationException("LLM API Key 未配置 (LLM:ApiKey 或 DeepSeek:ApiKey)");
        
        // API 基础 URL
        _baseUrl = configuration["LLM:BaseUrl"] 
            ?? configuration["DeepSeek:BaseUrl"] 
            ?? "https://api.openai.com";
        
        // Embedding API 端点（默认 OpenAI 标准格式）
        _embeddingEndpoint = configuration["LLM:EmbeddingEndpoint"] ?? "/v1/embeddings";
        
        // Embedding 模型
        _modelId = configuration["LLM:EmbeddingModel"] 
            ?? configuration["SemanticKernel:EmbeddingModel"] 
            ?? "text-embedding-ada-002";
        
        // 向量维度（不同模型维度不同）
        _vectorDimension = configuration.GetValue<int>("LLM:VectorDimension", 1536);
        
        // 提供商标识（用于日志）
        _provider = configuration["LLM:Provider"] ?? "OpenAI-Compatible";
    }

    /// <summary>
    /// 向量维度（从配置读取）
    /// </summary>
    public int VectorDimension => _vectorDimension;

    /// <summary>
    /// 获取模型属性
    /// </summary>
    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>
    {
        { "ModelId", _modelId },
        { "VectorDimension", _vectorDimension },
        { "Provider", _provider }
    };

    /// <summary>
    /// 批量生成文本向量
    /// </summary>
    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IList<string> data,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        if (data == null || data.Count == 0)
            throw new ArgumentException("文本列表不能为空", nameof(data));

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("[{Provider}] Embedding: 开始为 {Count} 段文本生成向量", _provider, data.Count);

        try
        {
            // 构建请求
            var request = new
            {
                model = _modelId,
                input = data.ToArray(),
                encoding_format = "float"
            };

            var jsonContent = JsonSerializer.Serialize(request);
            // 使用可配置的端点路径
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{_embeddingEndpoint}")
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            // OpenAI 兼容 API 认证方式：Bearer Token
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonResponse = JsonDocument.Parse(responseContent);

            var results = new List<ReadOnlyMemory<float>>();

            // 解析响应
            if (jsonResponse.RootElement.TryGetProperty("data", out var dataArray))
            {
                // 按 index 排序确保顺序正确
                var embeddings = dataArray.EnumerateArray()
                    .Select(item => new
                    {
                        Index = item.GetProperty("index").GetInt32(),
                        Embedding = item.GetProperty("embedding")
                            .EnumerateArray()
                            .Select(v => v.GetSingle())
                            .ToArray()
                    })
                    .OrderBy(x => x.Index)
                    .ToList();

                foreach (var item in embeddings)
                {
                    results.Add(new ReadOnlyMemory<float>(item.Embedding));
                }

                // 记录 Token 使用情况
                if (jsonResponse.RootElement.TryGetProperty("usage", out var usage) &&
                    usage.TryGetProperty("total_tokens", out var tokens))
                {
                    _logger.LogInformation("[{Provider}] Embedding: 完成，使用 {Tokens} tokens，耗时 {Ms}ms",
                        _provider, tokens.GetInt32(), stopwatch.ElapsedMilliseconds);
                }
            }

            if (results.Count != data.Count)
            {
                throw new InvalidOperationException(
                    $"Embedding 结果数量不匹配：期望 {data.Count}，实际 {results.Count}");
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Provider}] Embedding: 生成向量失败", _provider);
            throw;
        }
    }
}

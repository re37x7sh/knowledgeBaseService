using System.Diagnostics;
using System.Text;
using System.Text.Json;
using KnowledgeBaseService.Core.Entities;
using KnowledgeBaseService.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace KnowledgeBaseService.Infrastructure.Clients;

/// <summary>
/// 豆包Embedding 客户端实现
/// 使用豆包的 Embedding API
/// </summary>
public class DoubaoEmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    // 豆包嵌入模型
    private const string ModelName = "doubao-embedding-text-240715";
    // 豆包 API 端点
    private const string Endpoint = "/api/v3/embeddings";

    public DoubaoEmbeddingClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        // 从配置读取豆包 API Key（仍使用 DeepSeek:ApiKey 配置键名保持兼容）
        _apiKey = configuration["DeepSeek:ApiKey"] ?? throw new InvalidOperationException("Ark API Key (DeepSeek:ApiKey) not configured");
        // 豆包 API 基础 URL
        _baseUrl = configuration["DeepSeek:BaseUrl"] ?? "https://ark.cn-beijing.volces.com";
    }

    /// <summary>
    /// 获取单个文本的向量嵌入
    /// </summary>
    public async Task<EmbeddingResult> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be empty", nameof(text));

        var results = await GetEmbeddingsAsync(new List<string> { text }, cancellationToken);
        return results.FirstOrDefault() ?? throw new InvalidOperationException("Failed to get embedding");
    }

    /// <summary>
    /// 批量获取向量嵌入
    /// </summary>
    public async Task<List<EmbeddingResult>> GetEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts == null || texts.Count == 0)
            throw new ArgumentException("Texts list cannot be empty", nameof(texts));

        var stopwatch = Stopwatch.StartNew();
        var results = new List<EmbeddingResult>();

        try
        {
            // 构建请求
            var request = new
            {
                model = ModelName,
                input = texts,
                encoding_format = "float"
            };

            var jsonContent = JsonSerializer.Serialize(request);
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{Endpoint}")
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            // 豆包 API 认证方式：Bearer Token
            httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonResponse = JsonDocument.Parse(responseContent);

            // 解析响应
            if (jsonResponse.RootElement.TryGetProperty("data", out var dataArray))
            {
                var totalTokens = 0;
                if (jsonResponse.RootElement.TryGetProperty("usage", out var usage) &&
                    usage.TryGetProperty("total_tokens", out var tokens))
                {
                    totalTokens = tokens.GetInt32();
                }

                foreach (var item in dataArray.EnumerateArray())
                {
                    if (item.TryGetProperty("embedding", out var embedding) &&
                        item.TryGetProperty("index", out var index))
                    {
                        var vectorList = embedding.EnumerateArray()
                            .Select(v => v.GetSingle())
                            .ToArray();

                        int idx = index.GetInt32();
                        if (idx >= 0 && idx < texts.Count)
                        {
                            results.Add(new EmbeddingResult
                            {
                                Text = texts[idx],
                                Vector = vectorList,
                                Model = ModelName,
                                Tokens = totalTokens > 0 ? totalTokens / texts.Count : 0,
                                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                            });
                        }
                    }
                }
            }

            stopwatch.Stop();

            return results;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to get embeddings from Ark API: {ex.Message}", ex);
        }
    }
}

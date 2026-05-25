using System.Diagnostics;
using System.Text;
using System.Text.Json;
using KnowledgeBaseService.Core.Entities;
using KnowledgeBaseService.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace KnowledgeBaseService.Infrastructure.Clients;

/// <summary>
/// 豆包（ByteDance Ark）聊天客户端实现
/// 使用豆包的 Chat API
/// 官方文档：https://www.volcengine.com/docs/82379/1099320
/// </summary>
public class DoubaoChatClient : ILLMChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    // 豆包对话模型
    private const string ModelName = "doubao-1-5-pro-32k-250115";
    // 豆包 API 端点
    private const string Endpoint = "/api/v3/chat/completions";

    public DoubaoChatClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        // 从配置读取豆包 API Key（仍使用 DeepSeek:ApiKey 配置键名保持兼容）
        _apiKey = configuration["DeepSeek:ApiKey"] ?? throw new InvalidOperationException("Ark API Key (DeepSeek:ApiKey) not configured");
        // 豆包 API 基础 URL
        _baseUrl = configuration["DeepSeek:BaseUrl"] ?? "https://ark.cn-beijing.volces.com";
    }

    /// <summary>
    /// 获取聊天完成（单次请求）
    /// </summary>
    public async Task<string> GetCompletionAsync(List<ChatMessage> messages, float temperature = 0.7f, int maxTokens = 1024, CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count == 0)
            throw new ArgumentException("Messages list cannot be empty", nameof(messages));

        try
        {
            var request = BuildRequest(messages, temperature, maxTokens, stream: false);
            var jsonContent = JsonSerializer.Serialize(request);
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{Endpoint}")
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonResponse = JsonDocument.Parse(responseContent);

            // 解析豆包 API 响应格式
            if (jsonResponse.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var messageObj) &&
                    messageObj.TryGetProperty("content", out var content))
                {
                    return content.GetString() ?? string.Empty;
                }
            }

            throw new InvalidOperationException("Invalid response format from Ark API");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to get completion from Ark API: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 流式获取聊天完成
    /// </summary>
    public async IAsyncEnumerable<string> GetCompletionStreamAsync(List<ChatMessage> messages, float temperature = 0.7f, int maxTokens = 1024, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count == 0)
            throw new ArgumentException("Messages list cannot be empty", nameof(messages));

        var request = BuildRequest(messages, temperature, maxTokens, stream: true);
        var jsonContent = JsonSerializer.Serialize(request);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{Endpoint}")
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };

        // 豆包 API 认证方式：Bearer Token
        httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");

        using (var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();

            using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
            using (var reader = new StreamReader(stream))
            {
                string? line;
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                        continue;

                    var jsonLine = line.Substring(6);
                    if (jsonLine == "[DONE]")
                        break;

                    string? contentToYield = null;
                    try
                    {
                        var jsonDoc = JsonDocument.Parse(jsonLine);
                        // 豆包 API 流式响应格式解析
                        if (jsonDoc.RootElement.TryGetProperty("choices", out var choices) &&
                            choices.GetArrayLength() > 0)
                        {
                            var firstChoice = choices[0];
                            if (firstChoice.TryGetProperty("delta", out var delta) &&
                                delta.TryGetProperty("content", out var content))
                            {
                                contentToYield = content.GetString();
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // 忽略解析错误，继续处理下一行
                        continue;
                    }

                    if (!string.IsNullOrEmpty(contentToYield))
                        yield return contentToYield;
                }
            }
        }
    }

    /// <summary>
    /// 构建 API 请求
    /// </summary>
    private object BuildRequest(List<ChatMessage> messages, float temperature, int maxTokens, bool stream)
    {
        var formattedMessages = messages.Select(m => new
        {
            role = m.Role.ToString().ToLower(),
            content = m.Content
        }).ToList();

        return new
        {
            model = ModelName,
            messages = formattedMessages,
            temperature = Math.Clamp(temperature, 0f, 2f),
            max_tokens = Math.Min(maxTokens, 4096),
            stream = stream
        };
    }
}

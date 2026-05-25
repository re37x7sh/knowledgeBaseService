using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace KnowledgeBaseService.Infrastructure.SemanticKernel;

/// <summary>
/// OpenAI 兼容 Chat 服务的 Semantic Kernel 实现
/// 支持豆包、OpenAI、DeepSeek、Qwen 等兼容 OpenAI API 格式的提供商
/// 实现 IChatCompletionService 接口
/// </summary>
public class OpenAICompatibleChatCompletionService : IChatCompletionService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _chatEndpoint;
    private readonly string _modelId;
    private readonly string _provider;
    private readonly ILogger<OpenAICompatibleChatCompletionService> _logger;

    public OpenAICompatibleChatCompletionService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OpenAICompatibleChatCompletionService> logger)
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
        
        // Chat API 端点（默认 OpenAI 标准格式）
        _chatEndpoint = configuration["LLM:ChatEndpoint"] ?? "/v1/chat/completions";
        
        // Chat 模型
        _modelId = configuration["LLM:ChatModel"] 
            ?? configuration["SemanticKernel:ChatModel"] 
            ?? "gpt-4";
        
        // 提供商标识（用于日志）
        _provider = configuration["LLM:Provider"] ?? "OpenAI-Compatible";
    }

    /// <summary>
    /// 获取模型属性
    /// </summary>
    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>
    {
        { "ModelId", _modelId },
        { "Provider", _provider }
    };

    /// <summary>
    /// 获取聊天完成（非流式）
    /// </summary>
    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        if (chatHistory == null || chatHistory.Count == 0)
            throw new ArgumentException("聊天历史不能为空", nameof(chatHistory));

        _logger.LogInformation("[{Provider}] Chat: 开始生成回复，消息数量: {Count}", _provider, chatHistory.Count);

        try
        {
            var messages = chatHistory.Select(m => new
            {
                role = m.Role.Label.ToLowerInvariant(),
                content = m.Content ?? string.Empty
            }).ToArray();

            // 从 executionSettings 获取参数
            var temperature = 0.7f;
            var maxTokens = 2048;
            
            if (executionSettings is PromptExecutionSettings settings)
            {
                if (settings.ExtensionData?.TryGetValue("temperature", out var tempValue) == true)
                {
                    temperature = Convert.ToSingle(tempValue);
                }
                if (settings.ExtensionData?.TryGetValue("max_tokens", out var maxTokensValue) == true)
                {
                    maxTokens = Convert.ToInt32(maxTokensValue);
                }
            }

            var request = new
            {
                model = _modelId,
                messages = messages,
                temperature = temperature,
                max_tokens = maxTokens,
                stream = false
            };

            var jsonContent = JsonSerializer.Serialize(request);
            // 使用可配置的端点路径
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{_chatEndpoint}")
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonResponse = JsonDocument.Parse(responseContent);

            string content = string.Empty;
            if (jsonResponse.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentElement))
                {
                    content = contentElement.GetString() ?? string.Empty;
                }
            }

            _logger.LogInformation("[{Provider}] Chat: 回复生成完成，长度: {Length}", _provider, content.Length);

            return new List<ChatMessageContent>
            {
                new(AuthorRole.Assistant, content)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Provider}] Chat: 生成回复失败", _provider);
            throw;
        }
    }

    /// <summary>
    /// 获取聊天完成（流式）
    /// </summary>
    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (chatHistory == null || chatHistory.Count == 0)
            throw new ArgumentException("聊天历史不能为空", nameof(chatHistory));

        _logger.LogInformation("[{Provider}] Chat Stream: 开始流式生成回复，消息数量: {Count}", _provider, chatHistory.Count);

        var messages = chatHistory.Select(m => new
        {
            role = m.Role.Label.ToLowerInvariant(),
            content = m.Content ?? string.Empty
        }).ToArray();

        // 从 executionSettings 获取参数
        var temperature = 0.7f;
        var maxTokens = 2048;
        
        if (executionSettings is PromptExecutionSettings settings)
        {
            if (settings.ExtensionData?.TryGetValue("temperature", out var tempValue) == true)
            {
                temperature = Convert.ToSingle(tempValue);
            }
            if (settings.ExtensionData?.TryGetValue("max_tokens", out var maxTokensValue) == true)
            {
                maxTokens = Convert.ToInt32(maxTokensValue);
            }
        }

        var request = new
        {
            model = _modelId,
            messages = messages,
            temperature = temperature,
            max_tokens = maxTokens,
            stream = true
        };

        var jsonContent = JsonSerializer.Serialize(request);
        // 使用可配置的端点路径
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{_chatEndpoint}")
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };

        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        HttpResponseMessage? response = null;
        Stream? stream = null;
        StreamReader? reader = null;

        try
        {
            response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            reader = new StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data:")) continue;

                var data = line.Substring(5).Trim();
                if (data == "[DONE]") break;

                JsonDocument? jsonDoc = null;
                try
                {
                    jsonDoc = JsonDocument.Parse(data);
                    if (jsonDoc.RootElement.TryGetProperty("choices", out var choices) &&
                        choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];
                        if (firstChoice.TryGetProperty("delta", out var delta) &&
                            delta.TryGetProperty("content", out var contentElement))
                        {
                            var content = contentElement.GetString();
                            if (!string.IsNullOrEmpty(content))
                            {
                                yield return new StreamingChatMessageContent(AuthorRole.Assistant, content);
                            }
                        }
                    }
                }
                finally
                {
                    jsonDoc?.Dispose();
                }
            }

            _logger.LogInformation("[{Provider}] Chat Stream: 流式生成完成", _provider);
        }
        finally
        {
            reader?.Dispose();
            stream?.Dispose();
            response?.Dispose();
        }
    }
}

using System.Text;
using System.Text.Json;
using KnowledgeBaseService.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KnowledgeBaseService.Infrastructure.Clients;

/// <summary>
/// 豆包视觉模型客户端实现
/// 使用豆包的 Vision API 进行图片识别
/// Model: doubao-seed-1-6-vision-250815
/// </summary>
public class DoubaoVisionClient : IDoubaoVisionClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly ILogger<DoubaoVisionClient> _logger;
    
    // 豆包视觉模型
    private const string VisionModelName = "doubao-seed-1-6-vision-250815";
    // API 端点
    private const string Endpoint = "/api/v3/chat/completions";
    
    // 默认提示词
    private const string DefaultPrompt = @"请仔细分析这张图片，提取其中的所有文字信息和重要内容。
要求：
1. 如果图片中有文字，请完整提取所有文字内容
2. 描述图片的主要内容、场景和关键元素
3. 如果是表格、图表，请描述其结构和数据
4. 如果是截图或文档，请提取关键信息
5. 用简洁、准确的语言描述，便于后续检索

请直接输出提取的内容，不需要额外说明。";

    public DoubaoVisionClient(
        HttpClient httpClient, 
        IConfiguration configuration,
        ILogger<DoubaoVisionClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // 从配置读取 API Key（复用 DeepSeek:ApiKey）
        _apiKey = configuration["DeepSeek:ApiKey"] 
            ?? throw new InvalidOperationException("Ark API Key (DeepSeek:ApiKey) not configured");
        
        // 豆包 API 基础 URL
        _baseUrl = configuration["DeepSeek:BaseUrl"] ?? "https://ark.cn-beijing.volces.com";
    }

    /// <summary>
    /// 分析图片并提取文字描述
    /// </summary>
    public async Task<string> AnalyzeImageAsync(
        string imageBase64, 
        string? prompt = null, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            throw new ArgumentException("Image base64 cannot be empty", nameof(imageBase64));

        try
        {
            _logger.LogInformation("开始使用豆包视觉模型分析图片，Base64 长度: {Length}", imageBase64.Length);

            var requestBody = new
            {
                model = VisionModelName,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "text",
                                text = prompt ?? DefaultPrompt
                            },
                            new
                            {
                                type = "image_url",
                                image_url = new
                                {
                                    url = $"data:image/jpeg;base64,{imageBase64}"
                                }
                            }
                        }
                    }
                },
                temperature = 0.3f,  // 使用较低温度以提高准确性
                max_tokens = 2048    // 足够长度以完整提取内容
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{Endpoint}")
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("豆包视觉 API 调用失败: {StatusCode}, 响应: {Response}", 
                    response.StatusCode, responseContent);
                throw new InvalidOperationException(
                    $"Doubao Vision API 调用失败: {response.StatusCode}");
            }

            var jsonResponse = JsonDocument.Parse(responseContent);

            // 解析响应
            if (jsonResponse.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var messageObj) &&
                    messageObj.TryGetProperty("content", out var content))
                {
                    var extractedText = content.GetString() ?? string.Empty;
                    
                    _logger.LogInformation("豆包视觉模型分析完成，提取文本长度: {Length}", 
                        extractedText.Length);
                    
                    return extractedText;
                }
            }

            _logger.LogWarning("豆包视觉 API 响应格式异常: {Response}", responseContent);
            throw new InvalidOperationException("Invalid response format from Doubao Vision API");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "豆包视觉 API 网络请求失败");
            throw new InvalidOperationException($"Failed to call Doubao Vision API: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析豆包视觉 API 响应失败");
            throw new InvalidOperationException($"Failed to parse Vision API response: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 从图片流分析并提取文字描述
    /// </summary>
    public async Task<string> AnalyzeImageFromStreamAsync(
        Stream imageStream, 
        string? prompt = null, 
        CancellationToken cancellationToken = default)
    {
        if (imageStream == null)
            throw new ArgumentNullException(nameof(imageStream));

        try
        {
            _logger.LogInformation("正在将图片流转换为 Base64...");
            
            // 读取流并转换为 Base64
            using var memoryStream = new MemoryStream();
            await imageStream.CopyToAsync(memoryStream, cancellationToken);
            var imageBytes = memoryStream.ToArray();
            var base64String = Convert.ToBase64String(imageBytes);
            
            _logger.LogInformation("图片转换完成，大小: {Size} KB", imageBytes.Length / 1024);

            return await AnalyzeImageAsync(base64String, prompt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从图片流分析失败");
            throw;
        }
    }
}

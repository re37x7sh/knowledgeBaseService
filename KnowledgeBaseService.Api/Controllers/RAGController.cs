using KnowledgeBaseService.Application.DTOs;
using KnowledgeBaseService.Application.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace KnowledgeBaseService.Api.Controllers;

/// <summary>
/// RAG 查询 API 控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RAGController : ControllerBase
{
    private readonly IRAGService _ragService;
    private readonly ILogger<RAGController> _logger;

    public RAGController(IRAGService ragService, ILogger<RAGController> logger)
    {
        _ragService = ragService;
        _logger = logger;
    }

    /// <summary>
    /// 执行 RAG 查询（单次请求）
    /// </summary>
    /// <remarks>
    /// 基于问题向量化、搜索相关文档、构建提示词、LLM生成答案
    /// </remarks>
    [HttpPost("query")]
    [ProducesResponseType(typeof(RAGQueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RAGQueryResponse>> Query(
        [FromBody] RAGQueryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("RAG query: {Question}", request.Question);
            var response = await _ragService.QueryAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid RAG query: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing RAG query");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to execute RAG query" });
        }
    }

    /// <summary>
    /// 执行 RAG 流式查询
    /// </summary>
    /// <remarks>
    /// 返回 Server-Sent Events (SSE) 流，实时推送答案
    /// </remarks>
    [HttpPost("query-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task QueryStream(
        [FromBody] RAGQueryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await HttpContext.Response.WriteAsJsonAsync(new { error = "Question is required" });
                return;
            }

            _logger.LogInformation("RAG stream query: {Question}", request.Question);

            HttpContext.Response.ContentType = "text/event-stream";
            HttpContext.Response.Headers.Append("Cache-Control", "no-cache");

            // 配置 JSON 序列化选项（camelCase）
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // 流式发送内容块
            await foreach (var chunk in _ragService.QueryStreamAsync(request, cancellationToken))
            {
                // 检测是否为 sources 元数据
                if (chunk.StartsWith("[SOURCES]") && chunk.EndsWith("[/SOURCES]"))
                {
                    // 提取 sources JSON 并发送
                    var sourcesJson = chunk.Substring(9, chunk.Length - 19); // 去掉 [SOURCES] 和 [/SOURCES]
                    _logger.LogInformation("发送 sources 数据，长度: {Length}", sourcesJson.Length);
                    var streamData = new { type = "sources", data = sourcesJson };
                    var json = JsonSerializer.Serialize(streamData, jsonOptions);
                    await HttpContext.Response.WriteAsync($"data: {json}\n\n");
                }
                else
                {
                    // 正常的内容块
                    var streamData = new { type = "content", data = chunk };
                    var json = JsonSerializer.Serialize(streamData, jsonOptions);
                    await HttpContext.Response.WriteAsync($"data: {json}\n\n");
                }
                await HttpContext.Response.Body.FlushAsync(cancellationToken);
            }

            // 发送完成信号
            var doneData = new { type = "done", data = "" };
            await HttpContext.Response.WriteAsync($"data: {JsonSerializer.Serialize(doneData, jsonOptions)}\n\n");
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid stream query: {Message}", ex.Message);
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing stream query");
            HttpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await HttpContext.Response.WriteAsJsonAsync(new { error = "Failed to execute stream query" });
        }
    }

    /// <summary>
    /// WebSocket 连接用于实时交互
    /// </summary>
    [HttpGet("ws")]
    public async Task WebSocket(CancellationToken cancellationToken)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        _logger.LogInformation("WebSocket connected");

        try
        {
            var buffer = new byte[1024 * 4];
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            while (!result.CloseStatus.HasValue)
            {
                // WebSocket 消息处理在这里实现
                // 简化示例：接收查询，返回答案
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            }

            await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
        }
        finally
        {
            webSocket.Dispose();
        }
    }
}

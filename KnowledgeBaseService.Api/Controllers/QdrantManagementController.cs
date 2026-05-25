using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Core.Constants;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeBaseService.Api.Controllers;

/// <summary>
/// Qdrant 管理控制器
/// 用于管理向量数据库的配置和维护
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class QdrantManagementController : ControllerBase
{
    private readonly IQdrantHttpClient _qdrantClient;
    private readonly ILogger<QdrantManagementController> _logger;

    public QdrantManagementController(
        IQdrantHttpClient qdrantClient,
        ILogger<QdrantManagementController> logger)
    {
        _qdrantClient = qdrantClient;
        _logger = logger;
    }

    /// <summary>
    /// 更新集合的 Payload Schema（启用 BM25 全文搜索）
    /// </summary>
    /// <param name="collectionName">集合名称（可选，默认使用 "documents"）</param>
    /// <returns>更新结果</returns>
    /// <remarks>
    /// 此 API 用于更新已存在集合的 Payload Schema 配置。
    /// 如果集合是新创建的，配置会自动应用，无需手动调用此 API。
    ///
    /// 示例请求:
    /// POST /api/qdrantmanagement/update-schema
    /// POST /api/qdrantmanagement/update-schema?collectionName=documents
    /// </remarks>
    [HttpPost("update-schema")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> UpdatePayloadSchema(
        [FromQuery] string collectionName = QdrantConstants.DefaultCollectionName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                return BadRequest(new { error = "Collection name is required" });
            }

            _logger.LogInformation("开始更新集合 {Collection} 的 Payload Schema", collectionName);

            var success = await _qdrantClient.UpdatePayloadSchemaAsync(collectionName, HttpContext.RequestAborted);

            if (success)
            {
                return Ok(new
                {
                    message = $"Successfully updated payload schema for collection '{collectionName}'",
                    collectionName,
                    timestamp = DateTime.UtcNow
                });
            }
            else
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = $"Failed to update payload schema for collection '{collectionName}'" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新 Payload Schema 失败");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = ex.Message });
        }
    }

    /// <summary>
    /// 获取集合的 Payload Schema 配置
    /// </summary>
    /// <param name="collectionName">集合名称（可选，默认使用 "documents"）</param>
    /// <returns>Payload Schema 配置</returns>
    /// <remarks>
    /// 示例请求:
    /// GET /api/qdrantmanagement/schema
    /// GET /api/qdrantmanagement/schema?collectionName=documents
    /// </remarks>
    [HttpGet("schema")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetPayloadSchema(
        [FromQuery] string collectionName = QdrantConstants.DefaultCollectionName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                return BadRequest(new { error = "Collection name is required" });
            }

            var schema = await _qdrantClient.GetPayloadSchemaAsync(collectionName, HttpContext.RequestAborted);

            if (schema == null)
            {
                return NotFound(new
                {
                    error = $"Collection '{collectionName}' not found or has no payload schema",
                    collectionName
                });
            }

            return Ok(new
            {
                collectionName,
                payloadSchema = schema,
                hasBM25Support = schema.ContainsKey("content") &&
                                 schema["content"] is Dictionary<string, object> contentDict &&
                                 contentDict.TryGetValue("type", out var type) &&
                                 type?.ToString() == "text",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取 Payload Schema 失败");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = ex.Message });
        }
    }

    /// <summary>
    /// 获取集合信息
    /// </summary>
    /// <param name="collectionName">集合名称（可选，默认使用 "documents"）</param>
    /// <returns>集合信息</returns>
    /// <remarks>
    /// 示例请求:
    /// GET /api/qdrantmanagement/collection-info
    /// GET /api/qdrantmanagement/collection-info?collectionName=documents
    /// </remarks>
    [HttpGet("collection-info")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetCollectionInfo(
        [FromQuery] string collectionName = QdrantConstants.DefaultCollectionName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                return BadRequest(new { error = "Collection name is required" });
            }

            var info = await _qdrantClient.GetCollectionInfoAsync(collectionName, HttpContext.RequestAborted);

            if (info == null)
            {
                return NotFound(new
                {
                    error = $"Collection '{collectionName}' not found",
                    collectionName
                });
            }

            return Ok(new
            {
                collectionName,
                info,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取集合信息失败");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = ex.Message });
        }
    }

    /// <summary>
    /// 初始化集合（如果不存在则创建，如果存在则更新配置）
    /// </summary>
    /// <param name="collectionName">集合名称（可选，默认使用 "documents"）</param>
    /// <param name="vectorDimension">向量维度（可选，默认 1536）</param>
    /// <returns>初始化结果</returns>
    /// <remarks>
    /// 示例请求:
    /// POST /api/qdrantmanagement/init-collection
    /// POST /api/qdrantmanagement/init-collection?collectionName=documents&vectorDimension=1536
    /// </remarks>
    [HttpPost("init-collection")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> InitializeCollection(
        [FromQuery] string collectionName = QdrantConstants.DefaultCollectionName,
        [FromQuery] int vectorDimension = 1536)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                return BadRequest(new { error = "Collection name is required" });
            }

            if (vectorDimension <= 0)
            {
                return BadRequest(new { error = "Vector dimension must be greater than 0" });
            }

            _logger.LogInformation("初始化集合: {Collection}, 向量维度: {Dimension}",
                collectionName, vectorDimension);

            await _qdrantClient.InitializeCollectionAsync(collectionName, vectorDimension, HttpContext.RequestAborted);

            // 获取更新后的配置
            var schema = await _qdrantClient.GetPayloadSchemaAsync(collectionName, HttpContext.RequestAborted);

            return Ok(new
            {
                message = $"Collection '{collectionName}' initialized successfully",
                collectionName,
                vectorDimension,
                payloadSchema = schema,
                hasBM25Support = schema?.ContainsKey("content") == true,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化集合失败");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = ex.Message });
        }
    }
}

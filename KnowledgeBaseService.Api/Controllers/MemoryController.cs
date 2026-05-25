using KnowledgeBaseService.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeBaseService.Api.Controllers;

/// <summary>
/// 对话长期记忆 API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MemoryController : ControllerBase
{
    private readonly IConversationMemoryService _memoryService;
    private readonly ILogger<MemoryController> _logger;

    public MemoryController(
        IConversationMemoryService memoryService,
        ILogger<MemoryController> logger)
    {
        _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 保存对话记忆
    /// </summary>
    /// <param name="request">记忆保存请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>记忆ID</returns>
    [HttpPost("save")]
    public async Task<IActionResult> SaveMemory(
        [FromBody] SaveMemoryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            string memoryId;
            
            // 如果有元数据，使用带元数据的方法
            if (request.Metadata != null && request.Metadata.Count > 0)
            {
                memoryId = await _memoryService.SaveMemoryWithMetadataAsync(
                    request.UserId,
                    request.SessionId,
                    request.UserMessage,
                    request.AssistantMessage,
                    request.MemoryType ?? "fact",
                    request.ImportanceScore ?? 0.5,
                    request.Metadata,
                    cancellationToken);
            }
            else
            {
                memoryId = await _memoryService.SaveMemoryAsync(
                    request.UserId,
                    request.SessionId,
                    request.UserMessage,
                    request.AssistantMessage,
                    request.MemoryType ?? "fact",
                    request.ImportanceScore ?? 0.5,
                    cancellationToken);
            }

            return Ok(new { memoryId, message = "记忆保存成功" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存记忆失败");
            return StatusCode(500, new { error = "保存记忆失败" });
        }
    }

    /// <summary>
    /// 检索相关记忆（向量检索）
    /// </summary>
    /// <param name="request">检索请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>相关记忆列表</returns>
    [HttpPost("retrieve")]
    public async Task<IActionResult> RetrieveMemories(
        [FromBody] RetrieveMemoryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var memories = await _memoryService.RetrieveMemoriesAsync(
                request.UserId,
                request.Query,
                request.TopK ?? 5,
                request.MinScore ?? 0.6,
                request.MemoryType,
                cancellationToken);

            return Ok(new
            {
                count = memories.Count,
                memories = memories.Select(m => new
                {
                    id = m.Id,
                    sessionId = m.SessionId,
                    memoryType = m.MemoryType,
                    summary = m.Summary,
                    fullContent = m.FullContent,
                    similarityScore = m.SimilarityScore,
                    importanceScore = m.ImportanceScore,
                    createdAt = m.CreatedAt,
                    lastAccessedAt = m.LastAccessedAt
                })
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检索记忆失败");
            return StatusCode(500, new { error = "检索记忆失败" });
        }
    }

    /// <summary>
    /// 获取用户最近的记忆（时间序列）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="count">数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>最近记忆列表</returns>
    [HttpGet("{userId}/recent")]
    public async Task<IActionResult> GetRecentMemories(
        string userId,
        [FromQuery] int count = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var memories = await _memoryService.GetRecentMemoriesAsync(
                userId,
                count,
                cancellationToken);

            return Ok(new
            {
                count = memories.Count,
                memories = memories.Select(m => new
                {
                    id = m.Id,
                    sessionId = m.SessionId,
                    memoryType = m.MemoryType,
                    summary = m.Summary,
                    fullContent = m.FullContent,
                    importanceScore = m.ImportanceScore,
                    createdAt = m.CreatedAt
                })
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取最近记忆失败");
            return StatusCode(500, new { error = "获取最近记忆失败" });
        }
    }

    /// <summary>
    /// 清理低重要性记忆
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="keepTopN">保留前N条</param>
    /// <param name="cancellationToken">取消令牌</param>
    [HttpPost("{userId}/cleanup")]
    public async Task<IActionResult> CleanupMemories(
        string userId,
        [FromQuery] int keepTopN = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _memoryService.CleanupMemoriesAsync(userId, keepTopN, cancellationToken);
            return Ok(new { message = "记忆清理成功" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理记忆失败");
            return StatusCode(500, new { error = "清理记忆失败" });
        }
    }

    /// <summary>
    /// 删除用户的所有记忆
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    [HttpDelete("{userId}")]
    public async Task<IActionResult> DeleteUserMemories(
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _memoryService.DeleteUserMemoriesAsync(userId, cancellationToken);
            return Ok(new { message = "用户记忆删除成功" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除用户记忆失败");
            return StatusCode(500, new { error = "删除用户记忆失败" });
        }
    }
}

#region Request DTOs

/// <summary>
/// 保存记忆请求
/// </summary>
public class SaveMemoryRequest
{
    /// <summary>用户ID（老师ID，必需）</summary>
    public string UserId { get; set; } = string.Empty;
    
    /// <summary>会话ID（可选，用于关联同一会话的记忆）</summary>
    public string? SessionId { get; set; }
    
    /// <summary>用户消息/老师问题（必需）</summary>
    public string UserMessage { get; set; } = string.Empty;
    
    /// <summary>助手回复/系统回复（必需）</summary>
    public string AssistantMessage { get; set; } = string.Empty;
    
    /// <summary>
    /// 记忆类型（可选，默认 fact）
    /// 通用类型：fact（事实）、preference（偏好）、context（上下文）
    /// 教学场景：exam_analysis（试卷分析）、student_profile（学生画像）、
    ///          class_summary（班级汇总）、answer_pattern（答题规律）、teaching_insight（教学洞察）
    /// </summary>
    public string? MemoryType { get; set; }
    
    /// <summary>重要性评分（可选，0-1，默认 0.5）</summary>
    public double? ImportanceScore { get; set; }
    
    /// <summary>
    /// 元数据（可选，教学场景扩展）
    /// 支持字段：examId, examName, classId, className, subject, studentIds, studentName, metrics
    /// 示例：{ "examId": "exam_001", "className": "高二(3)班", "subject": "数学", "metrics": { "avgScore": 78 } }
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// 检索记忆请求
/// </summary>
public class RetrieveMemoryRequest
{
    /// <summary>用户ID（老师ID，必需）</summary>
    public string UserId { get; set; } = string.Empty;
    
    /// <summary>查询问题（必需，用于语义检索相关记忆）</summary>
    public string Query { get; set; } = string.Empty;
    
    /// <summary>返回前K条（可选，默认 5）</summary>
    public int? TopK { get; set; }
    
    /// <summary>最小相似度（可选，0-1，默认 0.6）</summary>
    public double? MinScore { get; set; }
    
    /// <summary>
    /// 记忆类型过滤（可选）
    /// 可指定多个类型，用逗号分隔：exam_analysis,student_profile
    /// </summary>
    public string? MemoryType { get; set; }
}

#endregion

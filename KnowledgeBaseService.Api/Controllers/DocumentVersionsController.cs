using KnowledgeBaseService.Application.DTOs;
using KnowledgeBaseService.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeBaseService.Api.Controllers;

/// <summary>
/// 文档版本管理 API 控制器
/// 提供版本控制、比较、回滚等功能
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DocumentVersionsController : ControllerBase
{
    private readonly IDocumentVersionService _versionService;
    private readonly IDocumentService _documentService;
    private readonly ILogger<DocumentVersionsController> _logger;

    public DocumentVersionsController(
        IDocumentVersionService versionService,
        IDocumentService documentService,
        ILogger<DocumentVersionsController> logger)
    {
        _versionService = versionService;
        _documentService = documentService;
        _logger = logger;
    }

    /// <summary>
    /// 获取文档的所有版本列表
    /// </summary>
    /// <param name="documentId">文档ID</param>
    /// <param name="skip">跳过数量（分页）</param>
    /// <param name="take">取数量（分页）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>版本列表</returns>
    [HttpGet("document/{documentId}")]
    [ProducesResponseType(typeof(List<VersionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<VersionResponse>>> GetDocumentVersions(
        [FromRoute] string documentId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting versions for document {DocumentId}", documentId);

            var versions = await _versionService.GetVersionsAsync(documentId, skip, take, cancellationToken);
            if (!versions.Any())
                return NotFound(new { error = "No versions found for this document" });

            var responses = versions.Select(v => new VersionResponse
            {
                Id = v.Id,
                DocumentId = v.DocumentId,
                VersionNumber = v.VersionNumber,
                Title = v.Title,
                Tag = v.Tag,
                ChangeLog = v.ChangeLog,
                ChangeSummary = v.ChangeSummary,
                Category = v.Category,
                CreatedBy = v.CreatedBy,
                CreatedAt = v.CreatedAt,
                IsCurrent = v.IsCurrent,
                ContentSize = v.ContentSize
            }).ToList();

            return Ok(responses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting versions for document {DocumentId}", documentId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to get versions" });
        }
    }

    /// <summary>
    /// 获取特定版本的完整内容
    /// </summary>
    /// <param name="versionId">版本ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>版本内容</returns>
    [HttpGet("{versionId}/content")]
    [ProducesResponseType(typeof(VersionContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VersionContentResponse>> GetVersionContent(
        [FromRoute] string versionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting content for version {VersionId}", versionId);

            var version = await _versionService.GetVersionByIdAsync(versionId, cancellationToken);
            if (version == null)
                return NotFound(new { error = "Version not found" });

            var response = new VersionContentResponse
            {
                Id = version.Id,
                VersionNumber = version.VersionNumber,
                Title = version.Title,
                Content = version.Content,
                CreatedBy = version.CreatedBy,
                CreatedAt = version.CreatedAt,
                ChangeLog = version.ChangeLog
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting content for version {VersionId}", versionId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to get version content" });
        }
    }

    /// <summary>
    /// 创建新版本
    /// </summary>
    /// <param name="request">创建版本请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>新创建的版本</returns>
    [HttpPost("create")]
    [ProducesResponseType(typeof(VersionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<VersionResponse>> CreateVersion(
        [FromBody] CreateVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.DocumentId) ||
                string.IsNullOrWhiteSpace(request.Content) ||
                string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest(new { error = "DocumentId, Content, and Title are required" });
            }

            _logger.LogInformation("Creating new version for document {DocumentId}", request.DocumentId);

            var version = await _versionService.CreateVersionAsync(
                request.DocumentId,
                request.Content,
                request.Title,
                request.ChangeLog,
                request.CreatedBy,
                request.Tag,
                request.Category,
                cancellationToken);

            var response = new VersionResponse
            {
                Id = version.Id,
                DocumentId = version.DocumentId,
                VersionNumber = version.VersionNumber,
                Title = version.Title,
                Tag = version.Tag,
                ChangeLog = version.ChangeLog,
                ChangeSummary = version.ChangeSummary,
                Category = version.Category,
                CreatedBy = version.CreatedBy,
                CreatedAt = version.CreatedAt,
                IsCurrent = version.IsCurrent,
                ContentSize = version.ContentSize
            };

            return CreatedAtAction(nameof(GetVersionContent), new { versionId = version.Id }, response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid version creation request: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating version");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to create version" });
        }
    }

    /// <summary>
    /// 比较两个版本
    /// </summary>
    /// <param name="documentId">文档ID</param>
    /// <param name="fromVersion">源版本号</param>
    /// <param name="toVersion">目标版本号</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>版本比较结果</returns>
    [HttpGet("document/{documentId}/compare")]
    [ProducesResponseType(typeof(CompareVersionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompareVersionResponse>> CompareVersions(
        [FromRoute] string documentId,
        [FromQuery] int fromVersion,
        [FromQuery] int toVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (fromVersion < 1 || toVersion < 1)
                return BadRequest(new { error = "Version numbers must be greater than 0" });

            _logger.LogInformation(
                "Comparing versions for document {DocumentId}: {FromVersion} -> {ToVersion}",
                documentId, fromVersion, toVersion);

            var comparison = await _versionService.CompareVersionsAsync(
                documentId,
                fromVersion,
                toVersion,
                cancellationToken);

            if (comparison == null)
                return NotFound(new { error = "One or both versions not found" });

            var response = new CompareVersionResponse
            {
                FromVersionNumber = comparison.FromVersionNumber,
                ToVersionNumber = comparison.ToVersionNumber,
                Diff = comparison.Diff,
                LinesAdded = comparison.LinesAdded,
                LinesRemoved = comparison.LinesRemoved,
                LinesModified = comparison.LinesModified
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comparing versions");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to compare versions" });
        }
    }

    /// <summary>
    /// 回滚到指定版本
    /// </summary>
    /// <param name="documentId">文档ID</param>
    /// <param name="targetVersion">目标版本号</param>
    /// <param name="reason">回滚原因</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    [HttpPost("document/{documentId}/rollback")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RollbackToVersion(
        [FromRoute] string documentId,
        [FromQuery] int targetVersion,
        [FromQuery] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (targetVersion < 1)
                return BadRequest(new { error = "Version number must be greater than 0" });

            _logger.LogInformation(
                "Rolling back document {DocumentId} to version {TargetVersion}. Reason: {Reason}",
                documentId, targetVersion, reason ?? "No reason provided");

            var success = await _versionService.RollbackToVersionAsync(
                documentId,
                targetVersion,
                reason,
                cancellationToken);

            if (!success)
                return NotFound(new { error = "Document or version not found" });

            return Ok(new { message = $"Successfully rolled back to version {targetVersion}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rolling back version");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to rollback version" });
        }
    }

    /// <summary>
    /// 为版本添加标签
    /// </summary>
    /// <param name="versionId">版本ID</param>
    /// <param name="tag">标签名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    [HttpPost("{versionId}/tag")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddTagToVersion(
        [FromRoute] string versionId,
        [FromQuery] string tag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tag))
                return BadRequest(new { error = "Tag cannot be empty" });

            _logger.LogInformation("Adding tag '{Tag}' to version {VersionId}", tag, versionId);

            var success = await _versionService.AddTagToVersionAsync(versionId, tag, cancellationToken);
            if (!success)
                return NotFound(new { error = "Version not found" });

            return Ok(new { message = $"Tag '{tag}' added successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding tag to version");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to add tag" });
        }
    }

    /// <summary>
    /// 删除版本
    /// </summary>
    /// <param name="versionId">版本ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    [HttpDelete("{versionId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteVersion(
        [FromRoute] string versionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting version {VersionId}", versionId);

            var success = await _versionService.DeleteVersionAsync(versionId, cancellationToken);
            if (!success)
                return NotFound(new { error = "Version not found or cannot delete current version" });

            return Ok(new { message = "Version deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting version");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to delete version" });
        }
    }

    /// <summary>
    /// 获取版本统计信息
    /// </summary>
    /// <param name="documentId">文档ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>统计信息</returns>
    [HttpGet("document/{documentId}/statistics")]
    [ProducesResponseType(typeof(VersionStatisticsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VersionStatisticsResponse>> GetVersionStatistics(
        [FromRoute] string documentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting version statistics for document {DocumentId}", documentId);

            var stats = await _versionService.GetVersionStatisticsAsync(documentId, cancellationToken);
            if (stats == null)
                return NotFound(new { error = "Document not found or has no versions" });

            var response = new VersionStatisticsResponse
            {
                DocumentId = stats.DocumentId,
                TotalVersions = stats.TotalVersions,
                FirstVersionDate = stats.FirstVersionDate,
                LastVersionDate = stats.LastVersionDate,
                AverageSize = stats.AverageSize,
                MaxSize = stats.MaxSize,
                MinSize = stats.MinSize,
                TotalSize = stats.TotalSize,
                TaggedVersions = stats.TaggedVersions,
                MostFrequentEditor = stats.MostFrequentEditor,
                Tags = stats.Tags
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting version statistics");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to get statistics" });
        }
    }

    /// <summary>
    /// 导出版本为文件
    /// </summary>
    /// <param name="versionId">版本ID</param>
    /// <param name="format">导出格式（markdown, text, html）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文件内容</returns>
    [HttpGet("{versionId}/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportVersion(
        [FromRoute] string versionId,
        [FromQuery] string format = "markdown",
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!new[] { "markdown", "text", "html" }.Contains(format.ToLower()))
                return BadRequest(new { error = "Invalid format. Supported: markdown, text, html" });

            _logger.LogInformation("Exporting version {VersionId} as {Format}", versionId, format);

            var (content, fileName) = await _versionService.ExportVersionAsync(versionId, format, cancellationToken);

            return File(content, GetContentType(format), fileName);
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { error = "Version not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting version");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to export version" });
        }
    }

    /// <summary>
    /// 获取当前活跃版本
    /// </summary>
    /// <param name="documentId">文档ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>当前版本</returns>
    [HttpGet("document/{documentId}/current")]
    [ProducesResponseType(typeof(VersionContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VersionContentResponse>> GetCurrentVersion(
        [FromRoute] string documentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting current version for document {DocumentId}", documentId);

            var version = await _versionService.GetCurrentVersionAsync(documentId, cancellationToken);
            if (version == null)
                return NotFound(new { error = "No current version found for this document" });

            var response = new VersionContentResponse
            {
                Id = version.Id,
                VersionNumber = version.VersionNumber,
                Title = version.Title,
                Content = version.Content,
                CreatedBy = version.CreatedBy,
                CreatedAt = version.CreatedAt,
                ChangeLog = version.ChangeLog
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current version");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to get current version" });
        }
    }

    #region Private Helpers

    private static string GetContentType(string format)
    {
        return format.ToLower() switch
        {
            "html" => "text/html",
            "text" => "text/plain",
            "markdown" or _ => "text/markdown"
        };
    }

    #endregion
}

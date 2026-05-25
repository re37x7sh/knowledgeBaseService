using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Core.Entities;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace KnowledgeBaseService.Application.Services;

/// <summary>
/// 提供完整的版本控制功能（数据库持久化）
/// </summary>
public class DocumentVersionService : IDocumentVersionService
{
    private readonly IDocumentVersionRepository _versionRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IRAGService _ragService;
    private readonly ILogger<DocumentVersionService> _logger;

    public DocumentVersionService(
        IDocumentVersionRepository versionRepository,
        IDocumentRepository documentRepository,
        IRAGService ragService,
        ILogger<DocumentVersionService> logger)
    {
        _versionRepository = versionRepository ?? throw new ArgumentNullException(nameof(versionRepository));
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 创建文档的新版本
    /// </summary>
    public async Task<DocumentVersion> CreateVersionAsync(
        string documentId,
        string content,
        string title,
        string? changeLog = null,
        string? createdBy = null,
        string? tag = null,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));

        try
        {
            // 取消之前的当前版本标记
            var currentVersion = await _versionRepository.GetCurrentVersionAsync(documentId);
            if (currentVersion != null)
            {
                currentVersion.IsCurrent = false;
                await _versionRepository.UpdateAsync(currentVersion);
            }

            // 获取下一个版本号
            var maxVersionNumber = await _versionRepository.GetMaxVersionNumberAsync(documentId);
            var versionNumber = maxVersionNumber + 1;

            // 计算内容哈希
            var contentHash = ComputeContentHash(content);

            // 计算内容大小
            var contentSize = Encoding.UTF8.GetByteCount(content);

            // 计算变更摘要
            var changeSummary = GenerateChangeSummary(currentVersion, content);

            var newVersion = new DocumentVersion
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = documentId,
                VersionNumber = versionNumber,
                Content = content,
                Title = title,
                Tag = tag,
                ChangeLog = changeLog,
                ChangeSummary = changeSummary,
                Category = category,
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                IsCurrent = true,
                ContentSize = contentSize,
                ContentHash = contentHash
            };

            await _versionRepository.InsertAsync(newVersion);

            _logger.LogInformation(
                "Created version {VersionNumber} for document {DocumentId} by {CreatedBy}",
                versionNumber, documentId, createdBy ?? "system");

            // 同步更新主文档内容
            var document = await _documentRepository.GetByIdAsync(documentId);
            if (document != null)
            {
                document.Content = content;
                document.Title = title;
                document.UpdatedAt = DateTime.UtcNow;
                await _documentRepository.UpdateAsync(document);
                
                _logger.LogInformation("Updated document {DocumentId} content with new version", documentId);
                
                // 重新索引到向量数据库（异步执行，不阻塞返回）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var metadata = new Dictionary<string, object>
                        {
                            { "document_id", documentId },
                            { "title", title },
                            { "category", category ?? string.Empty },
                            { "version", versionNumber }
                        };
                        
                        await _ragService.IndexDocumentAsync(documentId, content, metadata, cancellationToken);
                        _logger.LogInformation("Re-indexed document {DocumentId} version {Version} to vector database", documentId, versionNumber);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to re-index document {DocumentId} after version creation", documentId);
                    }
                }, cancellationToken);
            }

            return newVersion;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating version for document {DocumentId}", documentId);
            throw;
        }
    }

    /// <summary>
    /// 获取文档的所有版本
    /// </summary>
    public async Task<List<DocumentVersion>> GetVersionsAsync(
        string documentId,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var versions = await _versionRepository.GetVersionsPagedAsync(documentId, skip, take);
            return versions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting versions for document {DocumentId}", documentId);
            return new List<DocumentVersion>();
        }
    }

    /// <summary>
    /// 获取特定版本
    /// </summary>
    public async Task<DocumentVersion?> GetVersionByIdAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _versionRepository.GetByIdAsync(versionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting version {VersionId}", versionId);
            return null;
        }
    }

    /// <summary>
    /// 获取文档的特定版本号
    /// </summary>
    public async Task<DocumentVersion?> GetVersionByNumberAsync(
        string documentId,
        int versionNumber,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _versionRepository.GetVersionByNumberAsync(documentId, versionNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting version {VersionNumber} for document {DocumentId}", versionNumber, documentId);
            return null;
        }
    }

    /// <summary>
    /// 回滚到指定版本
    /// </summary>
    public async Task<bool> RollbackToVersionAsync(
        string documentId,
        int targetVersionNumber,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var targetVersion = await _versionRepository.GetVersionByNumberAsync(documentId, targetVersionNumber);
            if (targetVersion == null)
            {
                _logger.LogWarning("Target version {VersionNumber} not found for document {DocumentId}", targetVersionNumber, documentId);
                return false;
            }

            // 创建一个新版本作为回滚
            var maxVersionNumber = await _versionRepository.GetMaxVersionNumberAsync(documentId);
            var rollbackVersion = new DocumentVersion
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = documentId,
                VersionNumber = maxVersionNumber + 1,
                Content = targetVersion.Content,
                Title = targetVersion.Title,
                ChangeLog = $"Rollback to version {targetVersionNumber}. {reason}",
                Category = targetVersion.Category,
                CreatedBy = "system",
                CreatedAt = DateTime.UtcNow,
                IsCurrent = true,
                ContentSize = targetVersion.ContentSize,
                ContentHash = targetVersion.ContentHash,
                ChangeSummary = $"Rollback from version {targetVersionNumber}"
            };

            // 取消旧的当前版本标记
            await _versionRepository.SetCurrentVersionAsync(documentId, rollbackVersion.VersionNumber);

            // 插入回滚版本
            await _versionRepository.InsertAsync(rollbackVersion);

            _logger.LogInformation(
                "Rolled back document {DocumentId} to version {VersionNumber}. Reason: {Reason}",
                documentId, targetVersionNumber, reason ?? "No reason provided");

            // 同步更新主文档内容
            var document = await _documentRepository.GetByIdAsync(documentId);
            if (document != null)
            {
                document.Content = targetVersion.Content;
                document.Title = targetVersion.Title;
                document.UpdatedAt = DateTime.UtcNow;
                await _documentRepository.UpdateAsync(document);
                
                _logger.LogInformation("Updated document {DocumentId} content with rolled back version", documentId);
                
                // 重新索引到向量数据库（异步执行）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var metadata = new Dictionary<string, object>
                        {
                            { "document_id", documentId },
                            { "title", targetVersion.Title },
                            { "category", targetVersion.Category ?? string.Empty },
                            { "version", rollbackVersion.VersionNumber }
                        };
                        
                        await _ragService.IndexDocumentAsync(documentId, targetVersion.Content, metadata, cancellationToken);
                        _logger.LogInformation("Re-indexed document {DocumentId} after rollback to version {Version}", documentId, targetVersionNumber);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to re-index document {DocumentId} after rollback", documentId);
                    }
                }, cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rolling back document {DocumentId} to version {VersionNumber}", documentId, targetVersionNumber);
            return false;
        }
    }

    /// <summary>
    /// 比较两个版本
    /// </summary>
    public async Task<VersionComparison?> CompareVersionsAsync(
        string documentId,
        int fromVersionNumber,
        int toVersionNumber,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fromVersion = await _versionRepository.GetVersionByNumberAsync(documentId, fromVersionNumber);
            var toVersion = await _versionRepository.GetVersionByNumberAsync(documentId, toVersionNumber);

            if (fromVersion == null || toVersion == null)
            {
                _logger.LogWarning("Cannot compare versions. From: {FromVersion}, To: {ToVersion}", fromVersionNumber, toVersionNumber);
                return null;
            }

            var comparison = ComputeVersionDiff(fromVersion.Content, toVersion.Content);

            var result = new VersionComparison
            {
                FromVersionNumber = fromVersionNumber,
                ToVersionNumber = toVersionNumber,
                Diff = comparison.diff,
                LinesAdded = comparison.linesAdded,
                LinesRemoved = comparison.linesRemoved,
                LinesModified = comparison.linesModified,
                ComparedAt = DateTime.UtcNow
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comparing versions {FromVersion} and {ToVersion}", fromVersionNumber, toVersionNumber);
            return null;
        }
    }

    /// <summary>
    /// 为版本添加标签
    /// </summary>
    public async Task<bool> AddTagToVersionAsync(
        string versionId,
        string tag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await _versionRepository.GetByIdAsync(versionId);
            if (version == null)
            {
                _logger.LogWarning("Version {VersionId} not found", versionId);
                return false;
            }

            version.Tag = tag;
            var result = await _versionRepository.UpdateAsync(version);

            if (result)
                _logger.LogInformation("Added tag '{Tag}' to version {VersionId}", tag, versionId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding tag to version {VersionId}", versionId);
            return false;
        }
    }

    /// <summary>
    /// 删除版本
    /// </summary>
    public async Task<bool> DeleteVersionAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await _versionRepository.GetByIdAsync(versionId);
            if (version == null)
            {
                _logger.LogWarning("Version {VersionId} not found", versionId);
                return false;
            }

            // 不能删除当前版本
            if (version.IsCurrent)
            {
                _logger.LogWarning("Cannot delete current version {VersionId}", versionId);
                return false;
            }

            var result = await _versionRepository.DeleteAsync(versionId);

            if (result)
                _logger.LogInformation("Deleted version {VersionId}", versionId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting version {VersionId}", versionId);
            return false;
        }
    }

    /// <summary>
    /// 获取文档版本统计信息
    /// </summary>
    public async Task<VersionStatistics?> GetVersionStatisticsAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var versions = await _versionRepository.GetVersionsByDocumentIdAsync(documentId);
            if (versions.Count == 0)
            {
                _logger.LogWarning("No versions found for document {DocumentId}", documentId);
                return null;
            }

            var stats = new VersionStatistics
            {
                DocumentId = documentId,
                TotalVersions = versions.Count,
                FirstVersionDate = versions.OrderBy(v => v.CreatedAt).FirstOrDefault()?.CreatedAt,
                LastVersionDate = versions.OrderByDescending(v => v.CreatedAt).FirstOrDefault()?.CreatedAt,
                AverageSize = (long)versions.Average(v => v.ContentSize),
                MaxSize = versions.Max(v => v.ContentSize),
                MinSize = versions.Min(v => v.ContentSize),
                TotalSize = versions.Sum(v => v.ContentSize),
                TaggedVersions = versions.Count(v => !string.IsNullOrEmpty(v.Tag)),
                MostFrequentEditor = versions
                    .Where(v => !string.IsNullOrEmpty(v.CreatedBy))
                    .GroupBy(v => v.CreatedBy)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key,
                Tags = versions
                    .Where(v => !string.IsNullOrEmpty(v.Tag))
                    .Select(v => v.Tag!)
                    .Distinct()
                    .ToList()
            };

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting version statistics for document {DocumentId}", documentId);
            return null;
        }
    }

    /// <summary>
    /// 获取当前活跃版本
    /// </summary>
    public async Task<DocumentVersion?> GetCurrentVersionAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _versionRepository.GetCurrentVersionAsync(documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current version for document {DocumentId}", documentId);
            return null;
        }
    }

    /// <summary>
    /// 导出版本为文件
    /// </summary>
    public async Task<(byte[] content, string fileName)> ExportVersionAsync(
        string versionId,
        string format = "markdown",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await _versionRepository.GetByIdAsync(versionId);
            if (version == null)
                throw new InvalidOperationException("Version not found");

            var content = format.ToLower() switch
            {
                "html" => GenerateHtmlContent(version),
                "text" => Encoding.UTF8.GetBytes(version.Content),
                "markdown" or _ => GenerateMarkdownContent(version)
            };

            var fileName = $"{version.DocumentId}_v{version.VersionNumber}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.{GetFileExtension(format)}";

            return (content, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting version {VersionId}", versionId);
            throw;
        }
    }

    #region Private Helpers

    /// <summary>
    /// 计算内容哈希值
    /// </summary>
    private static string ComputeContentHash(string content)
    {
        using (var sha256 = SHA256.Create())
        {
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
            return Convert.ToBase64String(hash);
        }
    }

    /// <summary>
    /// 生成变更摘要
    /// </summary>
    private static string GenerateChangeSummary(DocumentVersion? previousVersion, string newContent)
    {
        if (previousVersion == null)
            return "Initial version";

        var previousLines = previousVersion.Content.Split('\n').Length;
        var newLines = newContent.Split('\n').Length;
        var lineDiff = newLines - previousLines;

        return lineDiff switch
        {
            > 0 => $"Added {lineDiff} lines",
            < 0 => $"Removed {Math.Abs(lineDiff)} lines",
            _ => "Content modified"
        };
    }

    /// <summary>
    /// 计算版本差异
    /// </summary>
    private static (string diff, int linesAdded, int linesRemoved, int linesModified) ComputeVersionDiff(
        string fromContent,
        string toContent)
    {
        var fromLines = fromContent.Split('\n');
        var toLines = toContent.Split('\n');

        var linesAdded = 0;
        var linesRemoved = 0;
        var linesModified = 0;

        var maxLines = Math.Max(fromLines.Length, toLines.Length);
        var diff = new StringBuilder();

        for (int i = 0; i < maxLines; i++)
        {
            var fromLine = i < fromLines.Length ? fromLines[i] : null;
            var toLine = i < toLines.Length ? toLines[i] : null;

            if (fromLine != toLine)
            {
                if (string.IsNullOrEmpty(fromLine))
                {
                    diff.AppendLine($"+ {toLine}");
                    linesAdded++;
                }
                else if (string.IsNullOrEmpty(toLine))
                {
                    diff.AppendLine($"- {fromLine}");
                    linesRemoved++;
                }
                else
                {
                    diff.AppendLine($"~ {fromLine}");
                    diff.AppendLine($"  → {toLine}");
                    linesModified++;
                }
            }
        }

        return (diff.ToString(), linesAdded, linesRemoved, linesModified);
    }

    /// <summary>
    /// 生成Markdown格式内容
    /// </summary>
    private static byte[] GenerateMarkdownContent(DocumentVersion version)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {version.Title}");
        sb.AppendLine();
        sb.AppendLine($"**版本号**: {version.VersionNumber}");
        sb.AppendLine($"**创建时间**: {version.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        if (!string.IsNullOrEmpty(version.Tag))
            sb.AppendLine($"**标签**: {version.Tag}");
        if (!string.IsNullOrEmpty(version.CreatedBy))
            sb.AppendLine($"**编辑者**: {version.CreatedBy}");
        if (!string.IsNullOrEmpty(version.ChangeLog))
            sb.AppendLine($"**变更说明**: {version.ChangeLog}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(version.Content);

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// 生成HTML格式内容
    /// </summary>
    private static byte[] GenerateHtmlContent(DocumentVersion version)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine($"<title>{version.Title} - v{version.VersionNumber}</title>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; }");
        sb.AppendLine(".header { border-bottom: 2px solid #ddd; padding-bottom: 10px; margin-bottom: 20px; }");
        sb.AppendLine(".meta { color: #666; font-size: 0.9em; }");
        sb.AppendLine(".content { margin-top: 20px; line-height: 1.6; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine($"<h1>{version.Title}</h1>");
        sb.AppendLine("<div class=\"meta\">");
        sb.AppendLine($"<p>版本号: {version.VersionNumber} | 创建时间: {version.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        if (!string.IsNullOrEmpty(version.Tag))
            sb.AppendLine($" | 标签: {version.Tag}");
        if (!string.IsNullOrEmpty(version.CreatedBy))
            sb.AppendLine($" | 编辑者: {version.CreatedBy}");
        sb.AppendLine("</p>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"content\">");
        sb.AppendLine(System.Net.WebUtility.HtmlEncode(version.Content).Replace("\n", "<br>"));
        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// 获取文件扩展名
    /// </summary>
    private static string GetFileExtension(string format)
    {
        return format.ToLower() switch
        {
            "html" => "html",
            "text" => "txt",
            "markdown" or _ => "md"
        };
    }

    #endregion
}

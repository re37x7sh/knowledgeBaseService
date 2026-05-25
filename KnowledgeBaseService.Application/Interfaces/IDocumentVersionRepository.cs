using KnowledgeBaseService.Core.Entities;

namespace KnowledgeBaseService.Application.Interfaces;

/// <summary>
/// 文档版本仓储接口
/// </summary>
public interface IDocumentVersionRepository
{
    /// <summary>
    /// 根据ID获取版本
    /// </summary>
    Task<DocumentVersion?> GetByIdAsync(string id);

    /// <summary>
    /// 获取指定文档的所有版本
    /// </summary>
    Task<List<DocumentVersion>> GetVersionsByDocumentIdAsync(string documentId);

    /// <summary>
    /// 获取指定文档的特定版本
    /// </summary>
    Task<DocumentVersion?> GetVersionByNumberAsync(string documentId, int versionNumber);

    /// <summary>
    /// 获取指定文档的当前版本
    /// </summary>
    Task<DocumentVersion?> GetCurrentVersionAsync(string documentId);

    /// <summary>
    /// 获取文档版本列表（分页）
    /// </summary>
    Task<List<DocumentVersion>> GetVersionsPagedAsync(string documentId, int skip, int take);

    /// <summary>
    /// 创建新版本
    /// </summary>
    Task<bool> InsertAsync(DocumentVersion version);

    /// <summary>
    /// 更新版本
    /// </summary>
    Task<bool> UpdateAsync(DocumentVersion version);

    /// <summary>
    /// 删除版本
    /// </summary>
    Task<bool> DeleteAsync(string versionId);

    /// <summary>
    /// 删除指定文档的所有版本
    /// </summary>
    Task<bool> DeleteVersionsByDocumentIdAsync(string documentId);

    /// <summary>
    /// 获取指定文档的最大版本号
    /// </summary>
    Task<int> GetMaxVersionNumberAsync(string documentId);

    /// <summary>
    /// 设置当前版本（只有一个版本是IsCurrent=true）
    /// </summary>
    Task<bool> SetCurrentVersionAsync(string documentId, int versionNumber);

    /// <summary>
    /// 根据标签获取版本
    /// </summary>
    Task<DocumentVersion?> GetVersionByTagAsync(string documentId, string tag);

    /// <summary>
    /// 获取指定文档在日期范围内的版本
    /// </summary>
    Task<List<DocumentVersion>> GetVersionsByDateRangeAsync(string documentId, DateTime startDate, DateTime endDate);
}

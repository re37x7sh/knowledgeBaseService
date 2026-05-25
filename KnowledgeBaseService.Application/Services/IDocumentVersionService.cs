using KnowledgeBaseService.Core.Entities;

namespace KnowledgeBaseService.Application.Services;

/// <summary>
/// 文档版本管理服务接口
/// </summary>
public interface IDocumentVersionService
{
    /// <summary>
    /// 创建文档的新版本
    /// </summary>
    /// <param name="documentId">文档ID</param>
    /// <param name="content">新版本内容</param>
    /// <param name="title">文档标题</param>
    /// <param name="changeLog">变更说明</param>
    /// <param name="createdBy">编辑者</param>
    /// <param name="tag">版本标签（可选）</param>
    /// <param name="category">分类（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>新创建的版本</returns>
    Task<DocumentVersion> CreateVersionAsync(
        string documentId,
        string content,
        string title,
        string? changeLog = null,
        string? createdBy = null,
        string? tag = null,
        string? category = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文档的所有版本
    /// </summary>
    /// <param name="documentId">文档ID</param>
    /// <param name="skip">跳过数量</param>
    /// <param name="take">取数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>版本列表</returns>
    Task<List<DocumentVersion>> GetVersionsAsync(
        string documentId,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取特定版本
    /// </summary>
    /// <param name="versionId">版本ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>版本对象</returns>
    Task<DocumentVersion?> GetVersionByIdAsync(
        string versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文档的特定版本号
    /// </summary>
    /// <param name="documentId">文档ID</param>
    /// <param name="versionNumber">版本号</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>版本对象</returns>
    Task<DocumentVersion?> GetVersionByNumberAsync(
        string documentId,
        int versionNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 回滚到指定版本
    /// </summary>
    /// <param name="documentId">文档ID</param>
    /// <param name="targetVersionNumber">目标版本号</param>
    /// <param name="reason">回滚原因</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功与否</returns>
    Task<bool> RollbackToVersionAsync(
        string documentId,
        int targetVersionNumber,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 比较两个版本
    /// </summary>
    /// <param name="documentId">文档ID</param>
    /// <param name="fromVersionNumber">源版本号</param>
    /// <param name="toVersionNumber">目标版本号</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>版本比较结果</returns>
    Task<VersionComparison?> CompareVersionsAsync(
        string documentId,
        int fromVersionNumber,
        int toVersionNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 为版本添加标签
    /// </summary>
    /// <param name="versionId">版本ID</param>
    /// <param name="tag">标签名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功与否</returns>
    Task<bool> AddTagToVersionAsync(
        string versionId,
        string tag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除版本
    /// </summary>
    /// <param name="versionId">版本ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功与否</returns>
    Task<bool> DeleteVersionAsync(
        string versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文档版本统计信息
    /// </summary>
    /// <param name="documentId">文档ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>统计信息</returns>
    Task<VersionStatistics?> GetVersionStatisticsAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前活跃版本
    /// </summary>
    /// <param name="documentId">文档ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>当前版本</returns>
    Task<DocumentVersion?> GetCurrentVersionAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 导出版本为文件
    /// </summary>
    /// <param name="versionId">版本ID</param>
    /// <param name="format">格式（markdown, text, html）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文件内容</returns>
    Task<(byte[] content, string fileName)> ExportVersionAsync(
        string versionId,
        string format = "markdown",
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 版本统计信息
/// </summary>
public class VersionStatistics
{
    /// <summary>
    /// 文档ID
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 总版本数
    /// </summary>
    public int TotalVersions { get; set; }

    /// <summary>
    /// 最早版本创建时间
    /// </summary>
    public DateTime? FirstVersionDate { get; set; }

    /// <summary>
    /// 最新版本创建时间
    /// </summary>
    public DateTime? LastVersionDate { get; set; }

    /// <summary>
    /// 平均版本大小（字节）
    /// </summary>
    public long AverageSize { get; set; }

    /// <summary>
    /// 最大版本大小（字节）
    /// </summary>
    public long MaxSize { get; set; }

    /// <summary>
    /// 最小版本大小（字节）
    /// </summary>
    public long MinSize { get; set; }

    /// <summary>
    /// 总存储大小（字节）
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// 已标记的版本数
    /// </summary>
    public int TaggedVersions { get; set; }

    /// <summary>
    /// 最常见的编辑者
    /// </summary>
    public string? MostFrequentEditor { get; set; }

    /// <summary>
    /// 版本标签列表
    /// </summary>
    public List<string> Tags { get; set; } = new();
}

using KnowledgeBaseService.Application.DTOs;
using KnowledgeBaseService.Core.Entities;

namespace KnowledgeBaseService.Application.Services;

/// <summary>
/// 文档管理服务接口
/// </summary>
public interface IDocumentService
{
    /// <summary>
    /// 创建文档
    /// </summary>
    Task<DocumentResponse> CreateAsync(CreateDocumentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文档
    /// </summary>
    Task<DocumentResponse?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出所有文档
    /// </summary>
    Task<List<DocumentResponse>> ListAsync(int skip = 0, int take = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除文档
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取内部文档对象
    /// </summary>
    Task<Document?> GetInternalAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新文档的文件扩展名
    /// </summary>
    Task<bool> UpdateFileExtensionAsync(string id, string fileExtension, CancellationToken cancellationToken = default);

    /// <summary>
    /// 外部接口：创建或追加文档内容
    /// </summary>
    Task<UpsertDocumentContentResponse> UpsertContentAsync(UpsertDocumentContentRequest request, CancellationToken cancellationToken = default);
}

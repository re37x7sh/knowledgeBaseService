using KnowledgeBaseService.Core.Entities;

namespace KnowledgeBaseService.Application.Interfaces;

/// <summary>
/// 文档仓储接口
/// </summary>
public interface IDocumentRepository
{
    /// <summary>
    /// 根据ID获取文档
    /// </summary>
    Task<Document?> GetByIdAsync(string id);

    /// <summary>
    /// 获取文档列表
    /// </summary>
    Task<List<Document>> GetListAsync(int skip, int take);

    /// <summary>
    /// 添加文档
    /// </summary>
    Task<bool> InsertAsync(Document document);

    /// <summary>
    /// 更新文档
    /// </summary>
    Task<bool> UpdateAsync(Document document);

    /// <summary>
    /// 删除文档（软删除）
    /// </summary>
    Task<bool> DeleteAsync(string id);
}

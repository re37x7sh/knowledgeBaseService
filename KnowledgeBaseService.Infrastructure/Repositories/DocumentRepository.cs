using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Core.Entities;
using SqlSugar;

namespace KnowledgeBaseService.Infrastructure.Repositories;

/// <summary>
/// 文档仓储实现 (SqlSugar)
/// </summary>
public class DocumentRepository : SimpleClient<Document>, IDocumentRepository
{
    public DocumentRepository(ISqlSugarClient context) : base(context)
    {
    }

    /// <summary>
    /// 根据ID获取文档
    /// </summary>
    public async Task<Document?> GetByIdAsync(string id)
    {
        return await base.GetByIdAsync(id);
    }

    /// <summary>
    /// 获取文档列表
    /// </summary>
    public async Task<List<Document>> GetListAsync(int skip, int take)
    {
        return await Context.Queryable<Document>()
            .Where(d => !d.IsDeleted)
            .OrderByDescending(d => d.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    /// <summary>
    /// 删除文档（软删除）
    /// </summary>
    public async Task<bool> DeleteAsync(string id)
    {
        // 软删除：更新 IsDeleted = true
        return await Context.Updateable<Document>()
            .SetColumns(d => d.IsDeleted == true)
            .SetColumns(d => d.UpdatedAt == DateTime.UtcNow)
            .Where(d => d.Id == id)
            .ExecuteCommandHasChangeAsync();
    }
}

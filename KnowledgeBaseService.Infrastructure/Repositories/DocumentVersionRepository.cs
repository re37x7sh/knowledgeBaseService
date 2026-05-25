using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Core.Entities;
using SqlSugar;

namespace KnowledgeBaseService.Infrastructure.Repositories;

/// <summary>
/// 文档版本仓储实现 (SqlSugar)
/// </summary>
public class DocumentVersionRepository : SimpleClient<DocumentVersion>, IDocumentVersionRepository
{
    public DocumentVersionRepository(ISqlSugarClient context) : base(context)
    {
    }

    /// <summary>
    /// 根据ID获取版本
    /// </summary>
    public async Task<DocumentVersion?> GetByIdAsync(string id)
    {
        return await Context.Queryable<DocumentVersion>()
            .Where(v => v.Id == id)
            .FirstAsync();
    }

    /// <summary>
    /// 获取指定文档的所有版本
    /// </summary>
    public async Task<List<DocumentVersion>> GetVersionsByDocumentIdAsync(string documentId)
    {
        return await Context.Queryable<DocumentVersion>()
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync();
    }

    /// <summary>
    /// 获取指定文档的特定版本
    /// </summary>
    public async Task<DocumentVersion?> GetVersionByNumberAsync(string documentId, int versionNumber)
    {
        return await Context.Queryable<DocumentVersion>()
            .Where(v => v.DocumentId == documentId && v.VersionNumber == versionNumber)
            .FirstAsync();
    }

    /// <summary>
    /// 获取指定文档的当前版本
    /// </summary>
    public async Task<DocumentVersion?> GetCurrentVersionAsync(string documentId)
    {
        return await Context.Queryable<DocumentVersion>()
            .Where(v => v.DocumentId == documentId && v.IsCurrent)
            .FirstAsync();
    }

    /// <summary>
    /// 获取文档版本列表（分页）
    /// </summary>
    public async Task<List<DocumentVersion>> GetVersionsPagedAsync(string documentId, int skip, int take)
    {
        return await Context.Queryable<DocumentVersion>()
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    /// <summary>
    /// 创建新版本
    /// </summary>
    public override async Task<bool> InsertAsync(DocumentVersion version)
    {
        var result = await Context.Insertable(version).ExecuteCommandAsync();
        return result > 0;
    }

    /// <summary>
    /// 更新版本
    /// </summary>
    public override async Task<bool> UpdateAsync(DocumentVersion version)
    {
        var result = await Context.Updateable(version).ExecuteCommandAsync();
        return result > 0;
    }

    /// <summary>
    /// 删除版本
    /// </summary>
    public async Task<bool> DeleteAsync(string versionId)
    {
        var result = await Context.Deleteable<DocumentVersion>()
            .Where(v => v.Id == versionId)
            .ExecuteCommandAsync();
        return result > 0;
    }

    /// <summary>
    /// 删除指定文档的所有版本
    /// </summary>
    public async Task<bool> DeleteVersionsByDocumentIdAsync(string documentId)
    {
        var result = await Context.Deleteable<DocumentVersion>()
            .Where(v => v.DocumentId == documentId)
            .ExecuteCommandAsync();
        return result > 0;
    }

    /// <summary>
    /// 获取指定文档的最大版本号
    /// </summary>
    public async Task<int> GetMaxVersionNumberAsync(string documentId)
    {
        var maxVersion = await Context.Queryable<DocumentVersion>()
            .Where(v => v.DocumentId == documentId)
            .MaxAsync(v => (int?)v.VersionNumber);
        
        return maxVersion ?? 0;
    }

    /// <summary>
    /// 设置当前版本（只有一个版本是IsCurrent=true）
    /// </summary>
    public async Task<bool> SetCurrentVersionAsync(string documentId, int versionNumber)
    {
        // 先将所有该文档的版本 IsCurrent 设置为 false
        await Context.Updateable<DocumentVersion>()
            .SetColumns(v => v.IsCurrent == false)
            .Where(v => v.DocumentId == documentId)
            .ExecuteCommandAsync();

        // 然后将指定版本的 IsCurrent 设置为 true
        var result = await Context.Updateable<DocumentVersion>()
            .SetColumns(v => v.IsCurrent == true)
            .Where(v => v.DocumentId == documentId && v.VersionNumber == versionNumber)
            .ExecuteCommandAsync();

        return result > 0;
    }

    /// <summary>
    /// 根据标签获取版本
    /// </summary>
    public async Task<DocumentVersion?> GetVersionByTagAsync(string documentId, string tag)
    {
        return await Context.Queryable<DocumentVersion>()
            .Where(v => v.DocumentId == documentId && v.Tag == tag)
            .FirstAsync();
    }

    /// <summary>
    /// 获取指定文档在日期范围内的版本
    /// </summary>
    public async Task<List<DocumentVersion>> GetVersionsByDateRangeAsync(string documentId, DateTime startDate, DateTime endDate)
    {
        return await Context.Queryable<DocumentVersion>()
            .Where(v => v.DocumentId == documentId 
                && v.CreatedAt >= startDate 
                && v.CreatedAt <= endDate)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();
    }
}

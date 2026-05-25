namespace KnowledgeBaseService.Core.Entities;

/// <summary>
/// 搜索结果实体
/// </summary>
public class SearchResult
{
    /// <summary>
    /// 文档ID
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 文档标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 文档内容摘要
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 相似度分数 (0-1)
    /// </summary>
    public float Score { get; set; }

    /// <summary>
    /// 文档分类
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 源URL
    /// </summary>
    public string? SourceUrl { get; set; }

    /// <summary>
    /// 高亮片段（包含查询词）
    /// </summary>
    public string? HighlightedSnippet { get; set; }
}

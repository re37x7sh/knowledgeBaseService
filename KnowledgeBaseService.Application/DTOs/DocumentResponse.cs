namespace KnowledgeBaseService.Application.DTOs;

/// <summary>
/// 文档响应 DTO
/// </summary>
public class DocumentResponse
{
    /// <summary>
    /// 文档ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 文档标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 文档分类
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 源URL
    /// </summary>
    public string? SourceUrl { get; set; }

    /// <summary>
    /// 文件扩展名
    /// </summary>
    public string? FileExtension { get; set; }

    /// <summary>
    /// 文档内容
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

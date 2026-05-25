namespace KnowledgeBaseService.Application.DTOs;

/// <summary>
/// 文档请求 DTO
/// </summary>
public class CreateDocumentRequest
{
    /// <summary>
    /// 文档标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 文档内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

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
}

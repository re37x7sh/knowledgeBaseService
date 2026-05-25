namespace KnowledgeBaseService.Application.DTOs;

/// <summary>
/// 外部同步文档内容的请求 DTO
/// </summary>
public class UpsertDocumentContentRequest
{
    /// <summary>
    /// 文档 ID，首次调用可以为空
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// 文档名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 文档内容（追加的增量文本）
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 文档分类（可选）
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// 来源地址（可选）
    /// </summary>
    public string? SourceUrl { get; set; }

    /// <summary>
    /// 追加内容时使用的分隔符（默认换行）
    /// </summary>
    public string? AppendDelimiter { get; set; }

    /// <summary>
    /// 变更说明（可选）
    /// </summary>
    public string? ChangeLog { get; set; }

    /// <summary>
    /// 更新者（可选）
    /// </summary>
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// 版本标签（可选）
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    /// 建议的文件扩展名（默认 .json）
    /// </summary>
    public string? FileExtension { get; set; }
}

using SqlSugar;

namespace KnowledgeBaseService.Core.Entities;

/// <summary>
/// 文档实体 - 知识库中的文档
/// </summary>
[SugarTable("Document")]
public class Document
{
    /// <summary>
    /// 文档唯一标识
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, Length = 36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 文档标题
    /// </summary>
    [SugarColumn(Length = 1000)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 文档内容
    /// </summary>
    [SugarColumn(ColumnDataType = "text")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 文档分类/标签
    /// </summary>
    [SugarColumn(Length = 500)]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 文档来源URL（可选）
    /// </summary>
    [SugarColumn(Length = 1000, IsNullable = true)]
    public string? SourceUrl { get; set; }

    /// <summary>
    /// 文件扩展名（如 .docx, .pdf, .xlsx 等）
    /// </summary>
    [SugarColumn(Length = 20, IsNullable = true)]
    public string? FileExtension { get; set; }

    /// <summary>
    /// 文档元数据（JSON格式）
    /// </summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? Metadata { get; set; }

    /// <summary>
    /// 向量在Qdrant中的唯一标识
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public ulong? PointId { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 是否已删除
    /// </summary>
    public bool IsDeleted { get; set; } = false;
}

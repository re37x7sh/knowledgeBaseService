using SqlSugar;

namespace KnowledgeBaseService.Core.Entities;

/// <summary>
/// 文档版本实体 - 记录文档的历史版本
/// </summary>
[SugarTable("DocumentVersion")]
public class DocumentVersion
{
    /// <summary>
    /// 版本唯一标识
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, Length = 36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 关联的文档ID
    /// </summary>
    [SugarColumn(Length = 36)]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 版本号（从1开始自增）
    /// </summary>
    public int VersionNumber { get; set; }

    /// <summary>
    /// 版本内容（快照）
    /// </summary>
    [SugarColumn(ColumnDataType = "text")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 版本标题
    /// </summary>
    [SugarColumn(Length = 1000)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 版本标签/Tag（用于标记重要版本）
    /// 例如：v1.0、release、draft 等
    /// </summary>
    [SugarColumn(Length = 500, IsNullable = true)]
    public string? Tag { get; set; }

    /// <summary>
    /// 版本变更说明/备注
    /// </summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? ChangeLog { get; set; }

    /// <summary>
    /// 变更摘要（概述本版本相对前一版本的变化）
    /// </summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? ChangeSummary { get; set; }

    /// <summary>
    /// 版本的分类/标签
    /// </summary>
    [SugarColumn(Length = 500, IsNullable = true)]
    public string? Category { get; set; }

    /// <summary>
    /// 版本创建者/编辑者
    /// </summary>
    [SugarColumn(Length = 255, IsNullable = true)]
    public string? CreatedBy { get; set; }

    /// <summary>
    /// 版本创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 版本是否是当前活动版本
    /// </summary>
    public bool IsCurrent { get; set; } = false;

    /// <summary>
    /// 文档元数据（JSON格式）
    /// </summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? Metadata { get; set; }

    /// <summary>
    /// 版本大小（字节）
    /// </summary>
    public long ContentSize { get; set; }

    /// <summary>
    /// 版本哈希值（用于内容完整性检验）
    /// </summary>
    [SugarColumn(Length = 500, IsNullable = true)]
    public string? ContentHash { get; set; }
}

/// <summary>
/// 版本比较结果
/// </summary>
public class VersionComparison
{
    /// <summary>
    /// 源版本号
    /// </summary>
    public int FromVersionNumber { get; set; }

    /// <summary>
    /// 目标版本号
    /// </summary>
    public int ToVersionNumber { get; set; }

    /// <summary>
    /// 差异分析结果
    /// </summary>
    public string? Diff { get; set; }

    /// <summary>
    /// 新增内容行数
    /// </summary>
    public int LinesAdded { get; set; }

    /// <summary>
    /// 删除内容行数
    /// </summary>
    public int LinesRemoved { get; set; }

    /// <summary>
    /// 修改内容行数
    /// </summary>
    public int LinesModified { get; set; }

    /// <summary>
    /// 比较时间
    /// </summary>
    public DateTime ComparedAt { get; set; } = DateTime.UtcNow;
}

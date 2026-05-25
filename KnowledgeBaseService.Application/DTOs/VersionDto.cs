namespace KnowledgeBaseService.Application.DTOs;

/// <summary>
/// 创建新版本请求
/// </summary>
public class CreateVersionRequest
{
    /// <summary>
    /// 文档ID
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 新版本内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 文档标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 变更说明
    /// </summary>
    public string? ChangeLog { get; set; }

    /// <summary>
    /// 编辑者
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// 版本标签
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    /// 文档分类
    /// </summary>
    public string? Category { get; set; }
}

/// <summary>
/// 版本信息响应
/// </summary>
public class VersionResponse
{
    /// <summary>
    /// 版本ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 文档ID
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 版本号
    /// </summary>
    public int VersionNumber { get; set; }

    /// <summary>
    /// 版本标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 版本标签
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    /// 变更说明
    /// </summary>
    public string? ChangeLog { get; set; }

    /// <summary>
    /// 变更摘要
    /// </summary>
    public string? ChangeSummary { get; set; }

    /// <summary>
    /// 分类
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// 编辑者
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 是否是当前版本
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// 内容大小（字节）
    /// </summary>
    public long ContentSize { get; set; }
}

/// <summary>
/// 版本内容响应
/// </summary>
public class VersionContentResponse
{
    /// <summary>
    /// 版本ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 版本号
    /// </summary>
    public int VersionNumber { get; set; }

    /// <summary>
    /// 版本标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 版本内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 编辑者
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 变更说明
    /// </summary>
    public string? ChangeLog { get; set; }
}

/// <summary>
/// 版本比较响应
/// </summary>
public class CompareVersionResponse
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
    /// 差异内容
    /// </summary>
    public string? Diff { get; set; }

    /// <summary>
    /// 新增行数
    /// </summary>
    public int LinesAdded { get; set; }

    /// <summary>
    /// 删除行数
    /// </summary>
    public int LinesRemoved { get; set; }

    /// <summary>
    /// 修改行数
    /// </summary>
    public int LinesModified { get; set; }
}

/// <summary>
/// 版本统计响应
/// </summary>
public class VersionStatisticsResponse
{
    /// <summary>
    /// 文档ID
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 总版本数
    /// </summary>
    public int TotalVersions { get; set; }

    /// <summary>
    /// 第一个版本创建时间
    /// </summary>
    public DateTime? FirstVersionDate { get; set; }

    /// <summary>
    /// 最后一个版本创建时间
    /// </summary>
    public DateTime? LastVersionDate { get; set; }

    /// <summary>
    /// 平均版本大小（字节）
    /// </summary>
    public long AverageSize { get; set; }

    /// <summary>
    /// 最大版本大小（字节）
    /// </summary>
    public long MaxSize { get; set; }

    /// <summary>
    /// 最小版本大小（字节）
    /// </summary>
    public long MinSize { get; set; }

    /// <summary>
    /// 总存储大小（字节）
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// 已标记的版本数
    /// </summary>
    public int TaggedVersions { get; set; }

    /// <summary>
    /// 最常见编辑者
    /// </summary>
    public string? MostFrequentEditor { get; set; }

    /// <summary>
    /// 所有标签列表
    /// </summary>
    public List<string> Tags { get; set; } = new();
}

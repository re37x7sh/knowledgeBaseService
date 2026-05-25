using System;

namespace KnowledgeBaseService.Application.DTOs;

/// <summary>
/// 外部同步文档内容的响应 DTO
/// </summary>
public class UpsertDocumentContentResponse
{
    /// <summary>
    /// 文档 ID
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 文档名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 是否为新建文档
    /// </summary>
    public bool Created { get; set; }

    /// <summary>
    /// 当前版本号
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// 文档内容长度
    /// </summary>
    public int ContentLength { get; set; }

    /// <summary>
    /// 文档分类
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 文档最近更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 说明信息
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

namespace KnowledgeBaseService.Application.DTOs;

/// <summary>
/// RAG 查询响应 DTO
/// </summary>
public class RAGQueryResponse
{
    /// <summary>
    /// 用户原始问题
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// AI 生成的答案
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// 相关来源文档
    /// </summary>
    public List<SourceReference> Sources { get; set; } = new();

    /// <summary>
    /// 查询处理时间(毫秒)
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// 使用的token数
    /// </summary>
    public int TokensUsed { get; set; }
}

/// <summary>
/// 源文档引用
/// </summary>
public class SourceReference
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
    /// 相似度分数
    /// </summary>
    public float Score { get; set; }

    /// <summary>
    /// 高亮片段
    /// </summary>
    public string Snippet { get; set; } = string.Empty;

    /// <summary>
    /// 源URL
    /// </summary>
    public string? SourceUrl { get; set; }

    /// <summary>
    /// 文件类型（image/pdf/docx等）
    /// </summary>
    public string? FileType { get; set; }

    /// <summary>
    /// 图片的Base64编码（仅当FileType为image时有值）
    /// </summary>
    public string? ImageBase64 { get; set; }

    /// <summary>
    /// 命中文本的上下文提示（用于图片定位）
    /// </summary>
    public string? MatchHint { get; set; }
}

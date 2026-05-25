namespace KnowledgeBaseService.Core.Entities;

/// <summary>
/// 向量嵌入结果
/// </summary>
public class EmbeddingResult
{
    /// <summary>
    /// 输入文本
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 向量值 (1536维)
    /// </summary>
    public float[]? Vector { get; set; }

    /// <summary>
    /// 向量维度
    /// </summary>
    public int Dimension => Vector?.Length ?? 0;

    /// <summary>
    /// 处理时间(毫秒)
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// 模型名称
    /// </summary>
    public string Model { get; set; } = "deepseek-embedding";

    /// <summary>
    /// 使用的token数
    /// </summary>
    public int Tokens { get; set; }
}

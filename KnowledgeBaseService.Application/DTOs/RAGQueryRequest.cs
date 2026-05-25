namespace KnowledgeBaseService.Application.DTOs;

/// <summary>
/// RAG 查询请求 DTO
/// </summary>
public class RAGQueryRequest
{
    /// <summary>
    /// 用户问题
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// 搜索结果数量
    /// </summary>
    public int TopK { get; set; } = 5;

    /// <summary>
    /// 相似度阈值
    /// </summary>
    public float ScoreThreshold { get; set; } = 0.5f;

    /// <summary>
    /// 采样温度 (0-2)
    /// </summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// 最大响应token数
    /// </summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>
    /// 限定的文档 ID 列表（可选）
    /// 为空或null时搜索全库，指定时仅搜索这些文档
    /// </summary>
    public List<string>? DocumentIds { get; set; }

    /// <summary>
    /// 是否启用混合模式（可选，默认false）
    /// 启用时：首先基于知识库回答，若知识库信息不足，AI 会自动补充通用知识
    /// 关闭时：严格基于知识库内容回答
    /// </summary>
    public bool EnableHybridMode { get; set; } = false;

    /// <summary>
    /// 是否使用混合检索（向量+BM25），默认 true
    /// 关闭时仅使用向量检索
    /// </summary>
    public bool EnableHybridSearch { get; set; } = true;

    /// <summary>
    /// 向量检索权重（0-1），默认 0.7
    /// 仅在启用混合检索时有效
    /// </summary>
    public float VectorWeight { get; set; } = 0.7f;

    /// <summary>
    /// BM25检索权重（0-1），默认 0.3
    /// 仅在启用混合检索时有效
    /// </summary>
    public float Bm25Weight { get; set; } = 0.3f;
}

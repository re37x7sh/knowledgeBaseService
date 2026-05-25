namespace KnowledgeBaseService.Core.Constants;

/// <summary>
/// Qdrant 常量
/// </summary>
public static class QdrantConstants
{
    /// <summary>
    /// 默认集合名称
    /// </summary>
    public const string DefaultCollectionName = "documents";

    /// <summary>
    /// 向量相似度阈值
    /// </summary>
    public const float SimilarityThreshold = 0.5f;

    /// <summary>
    /// 默认搜索结果数量
    /// </summary>
    public const int DefaultTopK = 5;

    /// <summary>
    /// 最大搜索结果数量
    /// </summary>
    public const int MaxTopK = 20;
}

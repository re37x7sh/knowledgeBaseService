namespace KnowledgeBaseService.Application.Interfaces;

/// <summary>
/// 混合检索服务接口
/// 结合向量检索和关键词检索（BM25），使用 RRF 算法融合结果
/// </summary>
public interface IHybridSearchService
{
    /// <summary>
    /// 混合检索（向量 + BM25）
    /// </summary>
    /// <param name="collectionName">集合名称</param>
    /// <param name="queryText">查询文本</param>
    /// <param name="queryVector">查询向量</param>
    /// <param name="topK">返回结果数量</param>
    /// <param name="vectorWeight">向量检索权重（0-1），默认 0.7</param>
    /// <param name="bm25Weight">BM25检索权重（0-1），默认 0.3</param>
    /// <param name="documentIds">过滤的文档 ID 列表（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>融合后的检索结果</returns>
    Task<List<HybridSearchResult>> SearchAsync(
        string collectionName,
        string queryText,
        float[] queryVector,
        int topK = 5,
        float vectorWeight = 0.7f,
        float bm25Weight = 0.3f,
        List<string>? documentIds = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 混合检索结果
/// </summary>
public class HybridSearchResult
{
    /// <summary>
    /// 点 ID
    /// </summary>
    public required ulong PointId { get; set; }

    /// <summary>
    /// 融合后的分数
    /// </summary>
    public required float Score { get; set; }

    /// <summary>
    /// 向量检索分数
    /// </summary>
    public float VectorScore { get; set; }

    /// <summary>
    /// BM25 检索分数
    /// </summary>
    public float Bm25Score { get; set; }

    /// <summary>
    /// 元数据
    /// </summary>
    public required Dictionary<string, object> Payload { get; set; }

    /// <summary>
    /// 来源类型（vector/bm25/hybrid）
    /// </summary>
    public string Source { get; set; } = "hybrid";
}

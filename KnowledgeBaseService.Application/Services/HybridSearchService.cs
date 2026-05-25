using KnowledgeBaseService.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeBaseService.Application.Services;

/// <summary>
/// 混合检索服务实现
/// 结合向量检索和关键词检索（BM25），使用 RRF 算法融合结果
/// </summary>
public class HybridSearchService : IHybridSearchService
{
    private readonly IQdrantHttpClient _qdrantClient;
    private readonly ILogger<HybridSearchService> _logger;

    // RRF 常数 k，通常设置为 60
    private const int RrfK = 60;

    public HybridSearchService(
        IQdrantHttpClient qdrantClient,
        ILogger<HybridSearchService> logger)
    {
        _qdrantClient = qdrantClient;
        _logger = logger;
    }

    /// <summary>
    /// 混合检索（向量 + BM25）
    /// </summary>
    public async Task<List<HybridSearchResult>> SearchAsync(
        string collectionName,
        string queryText,
        float[] queryVector,
        int topK = 5,
        float vectorWeight = 0.7f,
        float bm25Weight = 0.3f,
        List<string>? documentIds = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            throw new ArgumentException("Query text cannot be empty", nameof(queryText));

        if (queryVector == null || queryVector.Length == 0)
            throw new ArgumentException("Query vector cannot be empty", nameof(queryVector));

        if (Math.Abs(vectorWeight + bm25Weight - 1.0f) > 0.01f)
        {
            _logger.LogWarning("权重之和不为1，将自动归一化: vector={VectorWeight}, bm25={Bm25Weight}",
                vectorWeight, bm25Weight);
            var total = vectorWeight + bm25Weight;
            vectorWeight /= total;
            bm25Weight /= total;
        }

        _logger.LogInformation("开始混合检索: collection={Collection}, topK={TopK}, vectorWeight={VectorWeight}, bm25Weight={Bm25Weight}",
            collectionName, topK, vectorWeight, bm25Weight);

        // 并行执行向量检索和 BM25 检索
        var (vectorResults, bm25Results) = await Task.WhenAll(
            VectorSearchAsync(collectionName, queryVector, topK * 2, documentIds, cancellationToken),
            Bm25SearchAsync(collectionName, queryText, topK * 2, documentIds, cancellationToken)
        ).ContinueWith(t => (t.Result[0], t.Result[1]), cancellationToken);

        _logger.LogInformation("向量检索返回 {VectorCount} 条结果，BM25检索返回 {Bm25Count} 条结果",
            vectorResults.Count, bm25Results.Count);

        // 使用 RRF 融合结果
        var fusedResults = ReciprocalRankFusion(
            vectorResults,
            bm25Results,
            vectorWeight,
            bm25Weight);

        // 返回 topK 结果
        var finalResults = fusedResults
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        _logger.LogInformation("混合检索融合完成，返回 {Count} 条结果", finalResults.Count);

        // 记录前几条结果的信息
        foreach (var result in finalResults.Take(3))
        {
            var title = result.Payload.TryGetValue("title", out var titleObj) && titleObj is string t
                ? t
                : result.Payload.TryGetValue("document_id", out var docIdObj) && docIdObj is string d ? d : "Unknown";

            _logger.LogInformation("  - {Title}: Score={Score:F4}, Vector={VectorScore:F4}, BM25={Bm25Score:F4}",
                title, result.Score, result.VectorScore, result.Bm25Score);
        }

        return finalResults;
    }

    /// <summary>
    /// 向量检索
    /// </summary>
    private async Task<List<(ulong PointId, float Score, Dictionary<string, object> Payload)>> VectorSearchAsync(
        string collectionName,
        float[] queryVector,
        int topK,
        List<string>? documentIds,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _qdrantClient.SearchAsync(
                collectionName,
                queryVector,
                topK: topK,
                scoreThreshold: 0.3f,
                documentIds: documentIds,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "向量检索失败");
            return new List<(ulong, float, Dictionary<string, object>)>();
        }
    }

    /// <summary>
    /// BM25 检索
    /// </summary>
    private async Task<List<(ulong PointId, float Score, Dictionary<string, object> Payload)>> Bm25SearchAsync(
        string collectionName,
        string queryText,
        int topK,
        List<string>? documentIds,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _qdrantClient.SearchByTextAsync(
                collectionName,
                queryText,
                topK: topK,
                documentIds: documentIds,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BM25检索失败");
            return new List<(ulong, float, Dictionary<string, object>)>();
        }
    }

    /// <summary>
    /// RRF（Reciprocal Rank Fusion）融合算法
    /// 公式：score(d) = sum(w_i / (k + rank_i))
    /// </summary>
    private List<HybridSearchResult> ReciprocalRankFusion(
        List<(ulong PointId, float Score, Dictionary<string, object> Payload)> vectorResults,
        List<(ulong PointId, float Score, Dictionary<string, object> Payload)> bm25Results,
        float vectorWeight,
        float bm25Weight)
    {
        var fusedScores = new Dictionary<ulong, HybridSearchResult>();

        // 处理向量检索结果
        for (int rank = 0; rank < vectorResults.Count; rank++)
        {
            var (pointId, score, payload) = vectorResults[rank];

            if (!fusedScores.ContainsKey(pointId))
            {
                fusedScores[pointId] = new HybridSearchResult
                {
                    PointId = pointId,
                    Payload = payload,
                    VectorScore = 0,
                    Bm25Score = 0,
                    Score = 0
                };
            }

            // RRF 公式: weight / (k + rank)
            var rrfScore = vectorWeight / (RrfK + rank + 1);
            fusedScores[pointId].VectorScore = score; // 保存原始分数
            fusedScores[pointId].Score += rrfScore;
        }

        // 处理 BM25 检索结果
        for (int rank = 0; rank < bm25Results.Count; rank++)
        {
            var (pointId, score, payload) = bm25Results[rank];

            if (!fusedScores.ContainsKey(pointId))
            {
                fusedScores[pointId] = new HybridSearchResult
                {
                    PointId = pointId,
                    Payload = payload,
                    VectorScore = 0,
                    Bm25Score = 0,
                    Score = 0
                };
            }

            // RRF 公式: weight / (k + rank)
            var rrfScore = bm25Weight / (RrfK + rank + 1);
            fusedScores[pointId].Bm25Score = score; // 保存原始分数
            fusedScores[pointId].Score += rrfScore;
        }

        // 确定来源类型
        foreach (var (pointId, result) in fusedScores)
        {
            if (result.VectorScore > 0 && result.Bm25Score > 0)
            {
                result.Source = "hybrid";
            }
            else if (result.VectorScore > 0)
            {
                result.Source = "vector";
            }
            else
            {
                result.Source = "bm25";
            }
        }

        return fusedScores.Values.ToList();
    }
}

using KnowledgeBaseService.Core.Entities;

namespace KnowledgeBaseService.Application.Interfaces;

/// <summary>
/// Qdrant 向量数据库客户端接口
/// </summary>
public interface IQdrantHttpClient
{
    /// <summary>
    /// 初始化集合（如果不存在则创建）
    /// </summary>
    /// <param name="collectionName">集合名称</param>
    /// <param name="vectorDimension">向量维度</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task InitializeCollectionAsync(string collectionName, int vectorDimension, CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传向量点
    /// </summary>
    /// <param name="collectionName">集合名称</param>
    /// <param name="pointId">点ID</param>
    /// <param name="vector">向量数据</param>
    /// <param name="payload">元数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> UpsertPointAsync(string collectionName, ulong pointId, float[] vector, Dictionary<string, object> payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// 搜索相似向量
    /// </summary>
    /// <param name="collectionName">集合名称</param>
    /// <param name="vector">查询向量</param>
    /// <param name="topK">返回结果数量</param>
    /// <param name="scoreThreshold">分数阈值</param>
    /// <param name="documentIds">过滤的文档 ID 列表（可选，为空时搜索全库）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<(ulong PointId, float Score, Dictionary<string, object> Payload)>> SearchAsync(
        string collectionName, 
        float[] vector, 
        int topK = 5, 
        float scoreThreshold = 0.5f, 
        List<string>? documentIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除点
    /// </summary>
    /// <param name="collectionName">集合名称</param>
    /// <param name="pointId">点ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> DeletePointAsync(string collectionName, ulong pointId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取点数据
    /// </summary>
    /// <param name="collectionName">集合名称</param>
    /// <param name="pointId">点ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<(float[] Vector, Dictionary<string, object> Payload)?> GetPointAsync(string collectionName, ulong pointId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除文档的所有向量点
    /// </summary>
    /// <param name="collectionName">集合名称</param>
    /// <param name="documentId">文档ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> DeletePointsByDocumentIdAsync(string collectionName, string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除集合
    /// </summary>
    /// <param name="collectionName">集合名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取集合信息
    /// </summary>
    /// <param name="collectionName">集合名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Dictionary<string, object>?> GetCollectionInfoAsync(string collectionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 基于文本关键词搜索（BM25）
    /// </summary>
    /// <param name="collectionName">集合名称</param>
    /// <param name="queryText">查询文本</param>
    /// <param name="topK">返回结果数量</param>
    /// <param name="documentIds">过滤的文档 ID 列表（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<(ulong PointId, float Score, Dictionary<string, object> Payload)>> SearchByTextAsync(
        string collectionName,
        string queryText,
        int topK = 5,
        List<string>? documentIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新集合的 Payload Schema（用于启用 BM25 全文搜索等）
    /// </summary>
    /// <param name="collectionName">集合名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> UpdatePayloadSchemaAsync(string collectionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取集合的 Payload Schema 配置
    /// </summary>
    /// <param name="collectionName">集合名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Dictionary<string, object>?> GetPayloadSchemaAsync(string collectionName, CancellationToken cancellationToken = default);
}

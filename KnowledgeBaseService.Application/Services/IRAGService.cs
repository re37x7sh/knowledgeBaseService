using KnowledgeBaseService.Application.DTOs;

namespace KnowledgeBaseService.Application.Services;

/// <summary>
/// RAG 服务接口
/// 实现检索增强型生成的核心逻辑
/// </summary>
public interface IRAGService
{
    /// <summary>
    /// 执行 RAG 查询（单次请求）
    /// </summary>
    /// <param name="request">查询请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>RAG 查询响应</returns>
    Task<RAGQueryResponse> QueryAsync(RAGQueryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行 RAG 流式查询
    /// </summary>
    /// <param name="request">查询请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应流</returns>
    IAsyncEnumerable<string> QueryStreamAsync(RAGQueryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 索引文档（创建文档后调用此方法）
    /// </summary>
    /// <param name="documentId">文档ID</param>
    /// <param name="content">文档内容</param>
    /// <param name="metadata">元数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> IndexDocumentAsync(string documentId, string content, Dictionary<string, object> metadata, CancellationToken cancellationToken = default);
}

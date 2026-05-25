using KnowledgeBaseService.Core.Entities;

namespace KnowledgeBaseService.Application.Interfaces;

/// <summary>
///  向量化客户端接口
/// </summary>
public interface IEmbeddingClient
{
    /// <summary>
    /// 获取文本的向量嵌入
    /// </summary>
    /// <param name="text">输入文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>嵌入结果</returns>
    Task<EmbeddingResult> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量获取向量嵌入
    /// </summary>
    /// <param name="texts">文本列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>嵌入结果列表</returns>
    Task<List<EmbeddingResult>> GetEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken = default);
}

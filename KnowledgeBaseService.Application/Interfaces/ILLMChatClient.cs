using KnowledgeBaseService.Core.Entities;

namespace KnowledgeBaseService.Application.Interfaces;

/// <summary>
/// LLM 聊天客户端接口
/// </summary>
public interface ILLMChatClient
{
    /// <summary>
    /// 获取聊天完成
    /// </summary>
    /// <param name="messages">消息列表</param>
    /// <param name="temperature">采样温度 (0-2)</param>
    /// <param name="maxTokens">最大响应token数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应文本</returns>
    Task<string> GetCompletionAsync(List<ChatMessage> messages, float temperature = 0.7f, int maxTokens = 1024, CancellationToken cancellationToken = default);

    /// <summary>
    /// 流式获取聊天完成
    /// </summary>
    /// <param name="messages">消息列表</param>
    /// <param name="temperature">采样温度 (0-2)</param>
    /// <param name="maxTokens">最大响应token数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应流</returns>
    IAsyncEnumerable<string> GetCompletionStreamAsync(List<ChatMessage> messages, float temperature = 0.7f, int maxTokens = 1024, CancellationToken cancellationToken = default);
}

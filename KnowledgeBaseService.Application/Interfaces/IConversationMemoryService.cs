namespace KnowledgeBaseService.Application.Interfaces;

/// <summary>
/// 对话长期记忆服务接口
/// 结合向量检索（语义相似）和结构化查询（精确过滤）
/// </summary>
public interface IConversationMemoryService
{
    /// <summary>
    /// 保存对话记忆（自动向量化并存储到 Qdrant + PostgreSQL）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="sessionId">会话ID（可选）</param>
    /// <param name="userMessage">用户消息</param>
    /// <param name="assistantMessage">助手回复</param>
    /// <param name="memoryType">记忆类型（fact/preference/context/exam_analysis/student_profile/class_summary/answer_pattern/teaching_insight）</param>
    /// <param name="importanceScore">重要性评分（0-1）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>记忆ID</returns>
    Task<string> SaveMemoryAsync(
        string userId,
        string? sessionId,
        string userMessage,
        string assistantMessage,
        string memoryType = "fact",
        double importanceScore = 0.5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存对话记忆（带元数据，教学场景扩展）
    /// </summary>
    /// <param name="userId">用户ID（老师ID）</param>
    /// <param name="sessionId">会话ID（可选）</param>
    /// <param name="userMessage">老师问题</param>
    /// <param name="assistantMessage">系统回复</param>
    /// <param name="memoryType">记忆类型</param>
    /// <param name="importanceScore">重要性评分</param>
    /// <param name="metadata">元数据（examId, classId, studentIds, subject, metrics 等）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>记忆ID</returns>
    Task<string> SaveMemoryWithMetadataAsync(
        string userId,
        string? sessionId,
        string userMessage,
        string assistantMessage,
        string memoryType = "fact",
        double importanceScore = 0.5,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检索相关记忆（向量检索 + 用户过滤）
    /// </summary>
    /// <param name="userId">用户ID（必须，确保只检索该用户的记忆）</param>
    /// <param name="query">当前用户问题</param>
    /// <param name="topK">返回前K条记忆</param>
    /// <param name="minScore">最小相似度</param>
    /// <param name="memoryType">记忆类型过滤（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>相关记忆列表</returns>
    Task<List<MemorySearchResult>> RetrieveMemoriesAsync(
        string userId,
        string query,
        int topK = 5,
        double minScore = 0.6,
        string? memoryType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户的最近N条记忆（时间序列）
    /// </summary>
    Task<List<MemorySearchResult>> GetRecentMemoriesAsync(
        string userId,
        int count = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新记忆重要性（基于访问频次和时间衰减）
    /// </summary>
    Task UpdateMemoryImportanceAsync(
        string memoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理过期或低重要性记忆
    /// </summary>
    Task CleanupMemoriesAsync(
        string userId,
        int keepTopN = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除用户的所有记忆
    /// </summary>
    Task DeleteUserMemoriesAsync(
        string userId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 记忆检索结果
/// </summary>
public class MemorySearchResult
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string MemoryType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string FullContent { get; set; } = string.Empty;
    public double SimilarityScore { get; set; }
    public double ImportanceScore { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
}

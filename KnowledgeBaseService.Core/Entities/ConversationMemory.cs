using SqlSugar;

namespace KnowledgeBaseService.Core.Entities;

/// <summary>
/// 对话长期记忆实体
/// 结合向量检索（Qdrant）和结构化存储（PostgreSQL）
/// </summary>
[SugarTable("ConversationMemory")]
public class ConversationMemory
{
    /// <summary>
    /// 记忆唯一标识
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, Length = 36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// 用户ID - 区分不同用户的记忆（重要：隐私隔离）
    /// </summary>
    [SugarColumn(Length = 100, IsNullable = false)]
    public string UserId { get; set; } = string.Empty;
    
    /// <summary>
    /// 会话ID - 同一次对话的所有记忆
    /// </summary>
    [SugarColumn(Length = 36, IsNullable = true)]
    public string? SessionId { get; set; }
    
    /// <summary>
    /// 记忆类型
    /// 通用类型：
    /// - fact: 事实性记忆（"用户是高中数学老师"）
    /// - preference: 偏好记忆（"用户喜欢查看及格率和平均分"）
    /// - context: 上下文记忆（对话历史摘要）
    /// 
    /// 教学场景专用类型：
    /// - exam_analysis: 试卷分析记忆（"数学期中考试平均分78分，最高分98分"）
    /// - student_profile: 学生画像记忆（"李明同学近3次考试成绩下滑"）
    /// - class_summary: 班级汇总记忆（"高二(3)班共45人，本次全部提交"）
    /// - answer_pattern: 答题规律记忆（"第5题错误率达60%，主要错误类型是计算错误"）
    /// - teaching_insight: 教学洞察记忆（"本章知识点学生掌握较好，建议减少复习时间"）
    /// </summary>
    [SugarColumn(Length = 50)]
    public string MemoryType { get; set; } = "fact";
    
    /// <summary>
    /// 记忆摘要（用于结构化查询和展示）
    /// 例如："用户的职业是软件工程师"
    /// </summary>
    [SugarColumn(Length = 500)]
    public string Summary { get; set; } = string.Empty;
    
    /// <summary>
    /// 原始对话内容（完整上下文，包含用户问题和助手回复）
    /// JSON 格式：{ "user": "...", "assistant": "...", "context": [...] }
    /// </summary>
    [SugarColumn(ColumnDataType = "text")]
    public string FullContent { get; set; } = string.Empty;
    
    /// <summary>
    /// 向量化的内容片段（实际存入 Qdrant 的文本）
    /// 通常是 Summary + 关键上下文
    /// </summary>
    [SugarColumn(ColumnDataType = "text")]
    public string VectorContent { get; set; } = string.Empty;
    
    /// <summary>
    /// Qdrant 中的 Point ID（用于关联向量和结构化数据）
    /// </summary>
    [SugarColumn(Length = 100, IsNullable = true)]
    public string? VectorPointId { get; set; }
    
    /// <summary>
    /// 结构化元数据（JSON 格式）
    /// 通用格式：{ "topic": "工作", "entities": ["Python", "FastAPI"], "sentiment": "positive" }
    /// 
    /// 教学场景扩展：
    /// - examId: 试卷ID
    /// - classId: 班级ID
    /// - studentIds: 涉及的学生ID列表
    /// - subject: 学科
    /// - metrics: 关键指标 { "avgScore": 78, "passRate": 0.89, "submitCount": 45 }
    /// </summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? Metadata { get; set; }
    
    /// <summary>
    /// 重要性评分（0-1，用于记忆淘汰策略）
    /// - 高频访问 → 提升重要性
    /// - 长时间未访问 → 降低重要性
    /// - 用户明确标记的重要信息 → 固定高分
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public double ImportanceScore { get; set; } = 0.5;
    
    /// <summary>
    /// 访问次数（用于计算记忆强化）
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public int AccessCount { get; set; } = 0;
    
    /// <summary>
    /// 最后访问时间（用于时间衰减计算）
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 创建时间
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 过期时间（可选，用于临时记忆或会话记忆）
    /// null 表示永久记忆
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? ExpiresAt { get; set; }
    
    /// <summary>
    /// 是否已删除（软删除）
    /// </summary>
    [SugarColumn(IsNullable = false)]
    public bool IsDeleted { get; set; } = false;
}

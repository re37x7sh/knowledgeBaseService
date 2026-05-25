namespace KnowledgeBaseService.Core.Entities;

/// <summary>
/// 聊天消息角色枚举
/// </summary>
public enum MessageRole
{
    System,
    User,
    Assistant
}

/// <summary>
/// 聊天消息实体
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// 角色
    /// </summary>
    public MessageRole Role { get; set; }

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ChatMessage() { }

    public ChatMessage(MessageRole role, string content)
    {
        Role = role;
        Content = content;
        CreatedAt = DateTime.UtcNow;
    }
}

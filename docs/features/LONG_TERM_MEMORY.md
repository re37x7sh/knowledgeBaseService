# 长期记忆功能设计与实现

## 📋 概述

本文档描述如何在对话系统中集成知识库服务，实现**长期记忆**功能。通过结合**向量检索**（语义相似度）和**结构化存储**（精确过滤），为每个用户维护持久化的对话记忆。

## 🏗️ 架构设计

### 核心组件

```
┌──────────────────────────────────────────────────────────────┐
│                      对话应用 (Chat App)                       │
│  ┌────────────────────────────────────────────────────────┐  │
│  │  对话流程:                                              │  │
│  │  1. 用户输入问题                                        │  │
│  │  2. 检索长期记忆 (调用本服务 /api/memory/retrieve)      │  │
│  │  3. 构建完整上下文 = 短期记忆 + 长期记忆 + 知识库       │  │
│  │  4. 调用 LLM 生成回答                                   │  │
│  │  5. 保存对话到长期记忆 (调用 /api/memory/save)         │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────┬───────────────────────────────────────┘
                       │ HTTP API
┌──────────────────────┴───────────────────────────────────────┐
│             知识库服务 (KnowledgeBaseService)                 │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  ConversationMemoryService                          │    │
│  │  • 保存记忆：向量化 → Qdrant + PostgreSQL           │    │
│  │  • 检索记忆：向量检索 + 用户过滤 → 排序返回          │    │
│  │  • 记忆管理：重要性更新、过期清理                    │    │
│  └────────────┬────────────────┬────────────────────────┘    │
│               │                │                              │
│      ┌────────┴────────┐  ┌───┴───────────┐                 │
│      │ Qdrant          │  │ PostgreSQL    │                 │
│      │ (向量索引)       │  │ (结构化数据)   │                 │
│      │ • 语义检索       │  │ • 精确过滤     │                 │
│      │ • 相似度计算     │  │ • 关系查询     │                 │
│      └─────────────────┘  └───────────────┘                 │
└──────────────────────────────────────────────────────────────┘
```

### 数据流

```
用户问题
   │
   ▼
[1. 向量化查询] → Embedding API (Doubao 2560维)
   │
   ▼
[2. Qdrant 检索] → 按相似度返回 Top-K 记忆
   │              (过滤条件: user_id = xxx)
   ▼
[3. PostgreSQL 增强] → 获取完整记忆内容 + 元数据
   │
   ▼
[4. 重要性排序] → similarity_score * importance_score
   │
   ▼
返回最相关的记忆列表
```

## 💾 数据模型

### ConversationMemory 表结构

| 字段 | 类型 | 说明 |
|------|------|------|
| `Id` | string(36) | 主键 |
| `UserId` | string(100) | 用户ID（隔离不同用户的记忆） |
| `SessionId` | string(36) | 会话ID（可选，关联同一次对话） |
| `MemoryType` | string(50) | 记忆类型：`fact` / `preference` / `context` / `task` |
| `Summary` | string(500) | 记忆摘要（用于展示和结构化查询） |
| `FullContent` | text | 完整对话内容（JSON格式：`{"user": "...", "assistant": "..."}`) |
| `VectorContent` | text | 向量化的内容（实际存入 Qdrant） |
| `VectorPointId` | string(100) | Qdrant Point ID（关联向量） |
| `Metadata` | text | 元数据（JSON，如话题、实体、情感） |
| `ImportanceScore` | double | 重要性评分（0-1，用于淘汰策略） |
| `AccessCount` | int | 访问次数（用于记忆强化） |
| `LastAccessedAt` | datetime | 最后访问时间（时间衰减） |
| `CreatedAt` | datetime | 创建时间 |
| `ExpiresAt` | datetime | 过期时间（可选，临时记忆） |
| `IsDeleted` | bool | 软删除标记 |

### Qdrant Payload 结构

```json
{
  "memory_id": "uuid",
  "user_id": "user_123",
  "session_id": "session_456",
  "memory_type": "fact",
  "summary": "用户的职业是软件工程师",
  "created_at": "2026-02-04T10:30:00Z"
}
```

## 🔑 核心 API

### 1. 保存记忆

```http
POST /api/memory/save
Content-Type: application/json

{
  "userId": "user_123",
  "sessionId": "session_456",
  "userMessage": "我是一名软件工程师",
  "assistantMessage": "明白了，您是软件工程师。有什么我可以帮您的吗？",
  "memoryType": "fact",
  "importanceScore": 0.8
}
```

**响应**:
```json
{
  "memoryId": "mem_789",
  "message": "记忆保存成功"
}
```

### 2. 检索相关记忆

```http
POST /api/memory/retrieve
Content-Type: application/json

{
  "userId": "user_123",
  "query": "帮我推荐一个编程语言",
  "topK": 5,
  "minScore": 0.6,
  "memoryType": "fact"
}
```

**响应**:
```json
{
  "count": 2,
  "memories": [
    {
      "id": "mem_789",
      "sessionId": "session_456",
      "memoryType": "fact",
      "summary": "用户的职业是软件工程师",
      "fullContent": "{\"user\":\"我是一名软件工程师\",\"assistant\":\"...\"}",
      "similarityScore": 0.85,
      "importanceScore": 0.8,
      "createdAt": "2026-02-04T10:30:00Z",
      "lastAccessedAt": "2026-02-04T11:00:00Z"
    },
    {
      "id": "mem_790",
      "memoryType": "preference",
      "summary": "用户偏好 Python",
      "similarityScore": 0.72,
      "importanceScore": 0.6,
      "createdAt": "2026-02-03T15:20:00Z"
    }
  ]
}
```

### 3. 获取最近记忆

```http
GET /api/memory/user_123/recent?count=10
```

### 4. 清理记忆

```http
POST /api/memory/user_123/cleanup?keepTopN=100
```

### 5. 删除用户所有记忆

```http
DELETE /api/memory/user_123
```

## 🔄 集成示例

### Python 对话应用集成

```python
import requests
import json

class ChatWithMemory:
    def __init__(self, user_id, memory_service_url="http://localhost:5000"):
        self.user_id = user_id
        self.memory_service_url = memory_service_url
        self.session_id = str(uuid.uuid4())
        
    def chat(self, user_message):
        # 1. 检索相关长期记忆
        memories = self._retrieve_memories(user_message)
        
        # 2. 构建上下文
        context = self._build_context(memories)
        
        # 3. 调用 LLM（示例：使用 OpenAI）
        response = openai.ChatCompletion.create(
            model="gpt-4",
            messages=[
                {"role": "system", "content": f"你是一个智能助手。以下是用户的历史记忆：\n{context}"},
                {"role": "user", "content": user_message}
            ]
        )
        
        assistant_message = response.choices[0].message.content
        
        # 4. 保存对话到长期记忆
        self._save_memory(user_message, assistant_message)
        
        return assistant_message
    
    def _retrieve_memories(self, query):
        """检索相关记忆"""
        url = f"{self.memory_service_url}/api/memory/retrieve"
        payload = {
            "userId": self.user_id,
            "query": query,
            "topK": 5,
            "minScore": 0.6
        }
        response = requests.post(url, json=payload)
        if response.status_code == 200:
            return response.json()["memories"]
        return []
    
    def _save_memory(self, user_msg, assistant_msg):
        """保存记忆"""
        url = f"{self.memory_service_url}/api/memory/save"
        payload = {
            "userId": self.user_id,
            "sessionId": self.session_id,
            "userMessage": user_msg,
            "assistantMessage": assistant_msg,
            "memoryType": "context",
            "importanceScore": self._calculate_importance(user_msg)
        }
        requests.post(url, json=payload)
    
    def _build_context(self, memories):
        """构建上下文字符串"""
        if not memories:
            return "（无历史记忆）"
        
        context_lines = []
        for mem in memories:
            context_lines.append(f"- [{mem['memoryType']}] {mem['summary']}")
        
        return "\n".join(context_lines)
    
    def _calculate_importance(self, message):
        """计算重要性（简化版）"""
        # 可以使用 LLM 或规则判断重要性
        if len(message) > 50:
            return 0.7
        return 0.5

# 使用示例
chat = ChatWithMemory(user_id="user_123")
response = chat.chat("帮我推荐一个编程语言")
print(response)
```

### TypeScript/Node.js 集成

```typescript
interface Memory {
  id: string;
  summary: string;
  fullContent: string;
  similarityScore: number;
  importanceScore: number;
}

class ChatWithMemory {
  constructor(
    private userId: string,
    private memoryServiceUrl: string = "http://localhost:5000"
  ) {}

  async chat(userMessage: string): Promise<string> {
    // 1. 检索记忆
    const memories = await this.retrieveMemories(userMessage);
    
    // 2. 构建上下文
    const context = this.buildContext(memories);
    
    // 3. 调用 LLM
    const assistantMessage = await this.callLLM(userMessage, context);
    
    // 4. 保存记忆
    await this.saveMemory(userMessage, assistantMessage);
    
    return assistantMessage;
  }

  private async retrieveMemories(query: string): Promise<Memory[]> {
    const response = await fetch(`${this.memoryServiceUrl}/api/memory/retrieve`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        userId: this.userId,
        query,
        topK: 5,
        minScore: 0.6,
      }),
    });
    
    const data = await response.json();
    return data.memories;
  }

  private async saveMemory(userMsg: string, assistantMsg: string): Promise<void> {
    await fetch(`${this.memoryServiceUrl}/api/memory/save`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        userId: this.userId,
        userMessage: userMsg,
        assistantMessage: assistantMsg,
        memoryType: "context",
      }),
    });
  }

  private buildContext(memories: Memory[]): string {
    if (memories.length === 0) return "（无历史记忆）";
    return memories.map((m) => `- ${m.summary}`).join("\n");
  }

  private async callLLM(userMsg: string, context: string): Promise<string> {
    // 调用你的 LLM API
    // ...
    return "LLM 回复";
  }
}
```

## ⚙️ 配置说明

在 `appsettings.json` 中添加：

```json
{
  "Memory": {
    "DecayFactorDays": 30,        // 时间衰减因子（天）
    "MaxMemoriesPerUser": 1000    // 每个用户最大记忆数
  },
  "LLM": {
    "ApiKey": "your-api-key",
    "BaseUrl": "https://ark.cn-beijing.volces.com/api/v3",
    "VectorDimension": 2560
  }
}
```

## 🚀 部署步骤

### 1. 数据库初始化

`ConversationMemory` 表会在应用启动时自动创建（SqlSugar Code-First）。

### 2. 注册服务

在 [Program.cs](d:\dev\KnowledgeBaseService\KnowledgeBaseService.Api\Program.cs) 中添加：

```csharp
// 注册记忆服务
builder.Services.AddScoped<IConversationMemoryService, ConversationMemoryService>();
```

### 3. 初始化 Qdrant Collection

在 [ServiceInitializationHostedService.cs](d:\dev\KnowledgeBaseService\KnowledgeBaseService.Api\ServiceInitializationHostedService.cs) 中添加：

```csharp
public async Task StartAsync(CancellationToken cancellationToken)
{
    // ... 现有初始化代码 ...
    
    // 初始化记忆 collection
    var memoryService = serviceScope.ServiceProvider
        .GetRequiredService<IConversationMemoryService>();
    
    if (memoryService is ConversationMemoryService concreteService)
    {
        await concreteService.InitializeCollectionAsync(cancellationToken);
    }
}
```

### 4. 启动服务

```bash
cd KnowledgeBaseService.Api
dotnet run
```

访问 Swagger: `http://localhost:5000/swagger` 查看 Memory API。

## 📊 记忆管理策略

### 重要性计算

```
importance = base_score × decay_multiplier + access_boost

其中：
- decay_multiplier = 1 - (days_since_last_access / decay_factor)
- access_boost = min(0.05 × access_count, 0.3)
```

### 记忆淘汰

定期调用清理接口：

```bash
# 保留每个用户最重要的 100 条记忆
POST /api/memory/user_123/cleanup?keepTopN=100
```

### 记忆类型建议

| 类型 | 使用场景 | 示例 |
|------|---------|------|
| `fact` | 用户提供的事实信息 | "我住在北京" |
| `preference` | 用户偏好 | "我喜欢简洁的回答" |
| `context` | 对话历史摘要 | "之前讨论了 Python 性能优化" |
| `task` | 待办任务 | "需要在周五前完成报告" |

## 🔍 高级特性

### 1. 智能摘要提取

修改 `ExtractMemorySummaryAsync` 方法，调用 LLM 提取关键信息：

```csharp
private async Task<string> ExtractMemorySummaryAsync(
    string userMessage,
    string assistantMessage,
    CancellationToken cancellationToken)
{
    var prompt = $@"从以下对话中提取关键信息，生成简洁的摘要（不超过50字）：

用户：{userMessage}
助手：{assistantMessage}

摘要：";

    var summary = await _chatClient.GenerateResponseAsync(prompt, cancellationToken);
    return summary.Trim();
}
```

### 2. 实体提取

在 `Metadata` 中保存提取的实体：

```csharp
var metadata = new
{
    entities = new[] { "Python", "FastAPI", "性能优化" },
    topic = "编程",
    sentiment = "positive"
};
memory.Metadata = JsonSerializer.Serialize(metadata);
```

### 3. 混合检索

结合向量检索和关键词检索：

```csharp
// 1. 向量检索
var vectorResults = await _qdrantClient.SearchAsync(...);

// 2. 关键词检索
var keywordResults = await _dbClient.Queryable<ConversationMemory>()
    .Where(m => m.UserId == userId && m.Summary.Contains(keyword))
    .ToListAsync();

// 3. 合并去重
var combined = MergeAndDeduplicate(vectorResults, keywordResults);
```

## 📈 性能优化

### 1. 批量保存

```csharp
public async Task SaveMemoriesBatchAsync(List<ConversationMemory> memories)
{
    await _dbClient.Insertable(memories).ExecuteCommandAsync();
}
```

### 2. Redis 缓存

缓存高频访问的记忆：

```csharp
var cacheKey = $"memory:{userId}:{memoryId}";
var cached = await _redis.GetAsync<ConversationMemory>(cacheKey);
if (cached != null) return cached;
```

### 3. 异步清理

使用后台任务定期清理：

```csharp
// BackgroundServices/MemoryCleanupService.cs
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        // 每天清理一次
        await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        
        var users = await GetAllUserIds();
        foreach (var userId in users)
        {
            await _memoryService.CleanupMemoriesAsync(userId);
        }
    }
}
```

## 🛡️ 安全建议

1. **隐私隔离**: 强制检查 `userId`，确保用户只能访问自己的记忆
2. **数据加密**: 敏感记忆内容可以加密存储
3. **权限控制**: 添加 JWT 认证，验证用户身份
4. **审计日志**: 记录所有记忆访问和修改操作

## 📝 总结

通过结合 **Qdrant 向量检索**和 **PostgreSQL 结构化存储**，我们实现了：

✅ **语义检索**: 根据语义相似度找到相关记忆  
✅ **精确过滤**: 按用户ID、类型、时间过滤  
✅ **智能淘汰**: 基于重要性和访问频次的记忆管理  
✅ **隔离安全**: 每个用户独立的记忆空间  
✅ **灵活扩展**: 支持多种记忆类型和元数据  

这套架构可以直接集成到任何对话系统中，提供**真正的长期记忆**能力！

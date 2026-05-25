# 🧠 对话长期记忆系统 - 完整实现

## 概述

这是一个基于 **向量检索 + 结构化存储** 的长期记忆系统，可以集成到任何对话应用中，为每个用户提供持久化的个性化记忆能力。

### 核心特性

✅ **语义检索**: 使用 Qdrant 向量数据库，根据语义相似度检索相关记忆  
✅ **精确过滤**: PostgreSQL 结构化存储，支持按用户、类型、时间等维度查询  
✅ **智能管理**: 重要性评分、时间衰减、访问强化的自动记忆淘汰机制  
✅ **用户隔离**: 强制的 UserId 过滤，确保记忆隐私安全  
✅ **灵活扩展**: 支持多种记忆类型和自定义元数据  
✅ **零配置启动**: 复用现有 Qdrant + PostgreSQL 基础设施  

### 技术栈

- **向量检索**: Qdrant (2560维向量，余弦相似度)
- **结构化存储**: PostgreSQL + SqlSugar ORM
- **向量化**: Doubao/DeepSeek Embedding API
- **缓存**: Redis（可选）
- **API**: ASP.NET Core 8 RESTful API

---

## 📚 文档导航

### 快速开始

| 文档 | 内容 | 适用人群 |
|------|------|---------|
| [快速开始指南](./LONG_TERM_MEMORY_QUICKSTART.md) | 5分钟集成，包含完整测试示例 | **⭐ 推荐首先阅读** |
| [实现方案对比](./MEMORY_IMPLEMENTATION_COMPARISON.md) | 3种实现方案的详细对比 | 架构设计者 |
| [架构设计文档](./LONG_TERM_MEMORY.md) | 完整的设计思路和高级特性 | 技术深入了解 |
| [架构图集](./MEMORY_ARCHITECTURE_DIAGRAMS.md) | Mermaid 可视化图表 | 演示、PPT 制作 |

### 代码文件

| 文件 | 说明 |
|------|------|
| [ConversationMemory.cs](../KnowledgeBaseService.Core/Entities/ConversationMemory.cs) | 实体定义（数据模型） |
| [IConversationMemoryService.cs](../KnowledgeBaseService.Application/Interfaces/IConversationMemoryService.cs) | 服务接口 |
| [ConversationMemoryService.cs](../KnowledgeBaseService.Application/Services/ConversationMemoryService.cs) | 核心服务实现 |
| [MemoryController.cs](../KnowledgeBaseService.Api/Controllers/MemoryController.cs) | RESTful API |

---

## 🚀 快速开始

### 1. 注册服务

在 `Program.cs` 中添加：

```csharp
builder.Services.AddScoped<IConversationMemoryService, ConversationMemoryService>();
```

### 2. 初始化 Qdrant Collection

在 `ServiceInitializationHostedService.cs` 中添加：

```csharp
var memoryService = serviceScope.ServiceProvider
    .GetRequiredService<IConversationMemoryService>();

if (memoryService is ConversationMemoryService concreteService)
{
    await concreteService.InitializeCollectionAsync(cancellationToken);
}
```

### 3. 测试 API

```bash
# 保存记忆
curl -X POST http://localhost:5000/api/memory/save \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "user_001",
    "userMessage": "我是一名软件工程师",
    "assistantMessage": "明白了，您是软件工程师。",
    "memoryType": "fact",
    "importanceScore": 0.9
  }'

# 检索记忆
curl -X POST http://localhost:5000/api/memory/retrieve \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "user_001",
    "query": "推荐一个编程语言",
    "topK": 5
  }'
```

### 4. 在应用中集成

```python
import requests

class ChatBot:
    def __init__(self, user_id):
        self.user_id = user_id
        self.memory_api = "http://localhost:5000/api/memory"
    
    def chat(self, user_message):
        # 1. 检索记忆
        memories = self.retrieve_memories(user_message)
        
        # 2. 构建上下文
        context = "\n".join([m["summary"] for m in memories])
        
        # 3. 调用 LLM（带记忆）
        prompt = f"历史记忆：\n{context}\n\n用户：{user_message}"
        assistant_message = call_llm(prompt)
        
        # 4. 保存对话
        self.save_memory(user_message, assistant_message)
        
        return assistant_message
    
    def retrieve_memories(self, query):
        resp = requests.post(f"{self.memory_api}/retrieve", json={
            "userId": self.user_id,
            "query": query,
            "topK": 5
        })
        return resp.json()["memories"]
    
    def save_memory(self, user_msg, assistant_msg):
        requests.post(f"{self.memory_api}/save", json={
            "userId": self.user_id,
            "userMessage": user_msg,
            "assistantMessage": assistant_msg
        })
```

---

## 🏗️ 架构概览

### 数据流

```
用户问题
   ↓
[1. 向量化] → Doubao Embedding API (2560维)
   ↓
[2. Qdrant 检索] → 相似度排序 + 用户过滤
   ↓
[3. PostgreSQL 增强] → 获取完整内容 + 元数据
   ↓
[4. 重要性排序] → similarity × importance
   ↓
返回 Top-K 记忆
```

### 记忆类型

| 类型 | 说明 | 示例 |
|------|------|------|
| `fact` | 事实性记忆 | "我是软件工程师" |
| `preference` | 用户偏好 | "我喜欢简洁的代码" |
| `context` | 上下文记忆 | "之前讨论了 FastAPI" |
| `task` | 任务记忆 | "周五前完成报告" |

### 重要性计算

```
importance = base_score × (1 - days_since_access / 30) + 0.05 × access_count
```

- **时间衰减**: 长时间未访问的记忆降低重要性
- **访问强化**: 频繁访问的记忆提升重要性
- **自动淘汰**: 保留每个用户最重要的 N 条记忆

---

## 📊 API 参考

### 保存记忆

```http
POST /api/memory/save
Content-Type: application/json

{
  "userId": "user_123",
  "sessionId": "session_456",      // 可选
  "userMessage": "我喜欢 Python",
  "assistantMessage": "好的，记住了",
  "memoryType": "preference",      // 可选，默认 "fact"
  "importanceScore": 0.8           // 可选，默认 0.5
}

响应: { "memoryId": "mem_789", "message": "记忆保存成功" }
```

### 检索记忆

```http
POST /api/memory/retrieve
Content-Type: application/json

{
  "userId": "user_123",
  "query": "推荐一个 Python 框架",
  "topK": 5,                       // 可选，默认 5
  "minScore": 0.6,                 // 可选，默认 0.6
  "memoryType": "preference"       // 可选，过滤类型
}

响应: {
  "count": 2,
  "memories": [
    {
      "id": "mem_789",
      "summary": "用户喜欢 Python",
      "similarityScore": 0.85,
      "importanceScore": 0.8,
      "createdAt": "2026-02-04T10:30:00Z"
    }
  ]
}
```

### 其他 API

| API | 说明 |
|-----|------|
| `GET /api/memory/{userId}/recent?count=10` | 获取最近记忆 |
| `POST /api/memory/{userId}/cleanup?keepTopN=100` | 清理低重要性记忆 |
| `DELETE /api/memory/{userId}` | 删除用户所有记忆 |

---

## 🎯 使用场景

### 1. 个性化对话机器人

```
用户: "帮我推荐一个编程语言"
机器人: [检索记忆] → "您是后端工程师，喜欢简洁代码"
机器人: "推荐 Python + FastAPI，简洁高效..."
```

### 2. 智能客服

```
用户: "我的订单怎么查？"
客服: [检索记忆] → "您上次咨询过订单 #12345"
客服: "您可以在订单页面查看，或者我帮您查询..."
```

### 3. 学习助手

```
学生: "继续讲 Python"
助手: [检索记忆] → "上次讲到了列表推导式"
助手: "接下来讲字典推导式..."
```

### 4. 多轮对话上下文

```
第1轮: "我想学 Web 开发"
第2轮: "推荐一个框架" → [检索] → "您想学 Web 开发"
回复: "推荐 FastAPI 或 Django..."
```

---

## ⚙️ 配置选项

在 `appsettings.json` 中：

```json
{
  "Memory": {
    "DecayFactorDays": 30,           // 时间衰减因子（天）
    "MaxMemoriesPerUser": 1000       // 每个用户最大记忆数
  },
  "LLM": {
    "ApiKey": "your-api-key",
    "BaseUrl": "https://ark.cn-beijing.volces.com/api/v3",
    "VectorDimension": 2560          // 向量维度
  }
}
```

---

## 🔧 高级特性

### 1. 智能摘要提取

修改 `ExtractMemorySummaryAsync`，调用 LLM 提取关键信息：

```csharp
var prompt = $"从以下对话中提取关键信息：\n用户：{userMessage}\n助手：{assistantMessage}";
var summary = await _chatClient.GenerateResponseAsync(prompt);
```

### 2. 实体提取

在 `Metadata` 中保存提取的实体：

```csharp
memory.Metadata = JsonSerializer.Serialize(new {
    entities = new[] { "Python", "FastAPI" },
    topic = "编程",
    sentiment = "positive"
});
```

### 3. Redis 缓存

```csharp
var cacheKey = $"memory:{userId}:{memoryId}";
var cached = await _redis.GetAsync<ConversationMemory>(cacheKey);
if (cached != null) return cached;
```

### 4. 后台清理任务

```csharp
public class MemoryCleanupService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            // 清理所有用户的低重要性记忆
        }
    }
}
```

---

## 🛡️ 安全建议

1. **隐私隔离**: 强制检查 `userId`，确保用户只能访问自己的记忆
2. **数据加密**: 敏感内容可以加密存储
3. **权限控制**: 添加 JWT 认证
4. **审计日志**: 记录所有访问操作

---

## 📈 性能优化

| 优化项 | 方法 |
|--------|------|
| 查询延迟 | Redis 缓存热点记忆 |
| 存储成本 | 定期清理低重要性记忆 |
| 并发性能 | 批量向量检索 |
| 内存占用 | 分页查询，避免一次加载全部 |

---

## 🎓 学习资源

- [RAG 原理](./ARCHITECTURE.md) - 了解向量检索基础
- [Qdrant 文档](https://qdrant.tech/documentation/) - 向量数据库
- [Semantic Kernel](https://learn.microsoft.com/semantic-kernel/) - LLM 编排框架

---

## 🤝 贡献指南

欢迎贡献代码和文档！

1. Fork 本项目
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 打开 Pull Request

---

## 📄 许可证

本项目采用 MIT 许可证，详见 LICENSE 文件。

---

## 💬 联系方式

如有问题或建议，请提交 Issue 或联系维护者。

---

## 🎉 总结

通过本项目，你可以：

✅ **5分钟**集成长期记忆功能  
✅ 为对话系统提供**真正的个性化**能力  
✅ 复用现有基础设施，**零额外成本**  
✅ 完整的**生产级**实现，包含测试和文档  
✅ 灵活的架构，支持**任意扩展**  

立即开始：阅读 [快速开始指南](./LONG_TERM_MEMORY_QUICKSTART.md) 🚀

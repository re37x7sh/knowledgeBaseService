# 长期记忆功能快速开始

## 5分钟集成指南

### 1️⃣ 注册服务（已完成✅）

文件已创建：
- ✅ `Core/Entities/ConversationMemory.cs` - 实体定义
- ✅ `Application/Interfaces/IConversationMemoryService.cs` - 服务接口
- ✅ `Application/Services/ConversationMemoryService.cs` - 服务实现
- ✅ `Api/Controllers/MemoryController.cs` - API 控制器

### 2️⃣ 配置 DI（需要手动操作）

在 `Program.cs` 中添加：

```csharp
// 在 RAG 服务注册之后添加（约 L110）
builder.Services.AddScoped<IConversationMemoryService, ConversationMemoryService>();
```

### 3️⃣ 初始化 Qdrant Collection（需要手动操作）

在 `ServiceInitializationHostedService.cs` 的 `StartAsync` 方法中添加：

```csharp
// 在现有的 collection 初始化之后添加
try
{
    var memoryService = serviceScope.ServiceProvider
        .GetRequiredService<IConversationMemoryService>();
    
    if (memoryService is ConversationMemoryService concreteService)
    {
        await concreteService.InitializeCollectionAsync(cancellationToken);
        _logger.LogInformation("记忆服务初始化成功");
    }
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "记忆服务初始化失败（可能是 Qdrant 未启动）");
}
```

### 4️⃣ 测试 API

启动服务：

```bash
cd KnowledgeBaseService.Api
dotnet run
```

#### 测试 1: 保存记忆

```bash
curl -X POST http://localhost:5000/api/memory/save \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "test_user_001",
    "sessionId": "session_123",
    "userMessage": "我是一名软件工程师，专注于后端开发",
    "assistantMessage": "明白了，您是后端工程师。我会为您提供相关的技术建议。",
    "memoryType": "fact",
    "importanceScore": 0.9
  }'
```

#### 测试 2: 检索记忆

```bash
curl -X POST http://localhost:5000/api/memory/retrieve \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "test_user_001",
    "query": "帮我推荐一个后端框架",
    "topK": 5,
    "minScore": 0.6
  }'
```

#### 测试 3: 查看最近记忆

```bash
curl http://localhost:5000/api/memory/test_user_001/recent?count=10
```

### 5️⃣ 在对话应用中集成

#### Python 示例

```python
import requests

class ChatBot:
    def __init__(self, user_id):
        self.user_id = user_id
        self.memory_api = "http://localhost:5000/api/memory"
    
    def chat(self, user_message):
        # 1. 检索相关记忆
        memories = self.retrieve_memories(user_message)
        
        # 2. 构建上下文
        context = "\n".join([m["summary"] for m in memories])
        
        # 3. 调用 LLM（示例）
        prompt = f"历史记忆：\n{context}\n\n用户问题：{user_message}"
        assistant_message = self.call_llm(prompt)
        
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
            "assistantMessage": assistant_msg,
            "memoryType": "context"
        })
    
    def call_llm(self, prompt):
        # 调用你的 LLM API
        return "这是 LLM 的回复"

# 使用
bot = ChatBot("user_123")
response = bot.chat("帮我推荐一个 Python 框架")
print(response)
```

#### TypeScript 示例

```typescript
class ChatBot {
  constructor(private userId: string) {}

  async chat(userMessage: string): Promise<string> {
    // 1. 检索记忆
    const memories = await this.retrieveMemories(userMessage);
    
    // 2. 构建上下文
    const context = memories.map(m => m.summary).join("\n");
    
    // 3. 调用 LLM
    const assistantMessage = await this.callLLM(userMessage, context);
    
    // 4. 保存记忆
    await this.saveMemory(userMessage, assistantMessage);
    
    return assistantMessage;
  }

  private async retrieveMemories(query: string) {
    const resp = await fetch("http://localhost:5000/api/memory/retrieve", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ userId: this.userId, query, topK: 5 })
    });
    const data = await resp.json();
    return data.memories;
  }

  private async saveMemory(userMsg: string, assistantMsg: string) {
    await fetch("http://localhost:5000/api/memory/save", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        userId: this.userId,
        userMessage: userMsg,
        assistantMessage: assistantMsg,
        memoryType: "context"
      })
    });
  }

  private async callLLM(userMsg: string, context: string): Promise<string> {
    // 调用你的 LLM API
    return "LLM 回复";
  }
}
```

## 🎯 关键配置

在 `appsettings.json` 中添加（可选）：

```json
{
  "Memory": {
    "DecayFactorDays": 30,
    "MaxMemoriesPerUser": 1000
  }
}
```

## 📊 数据流程图

```
用户输入
   ↓
[检索长期记忆] ← Qdrant 向量检索（相似度排序）
   ↓             + PostgreSQL 过滤（user_id）
[构建上下文]
   ↓
   + 短期记忆（上下文窗口）
   + 长期记忆（检索到的历史）
   + 知识库（RAG 检索）
   ↓
[调用 LLM]
   ↓
[生成回复]
   ↓
[保存记忆] → Qdrant + PostgreSQL
```

## 🔧 常见问题

### Q1: 记忆没有保存成功？

检查：
1. Qdrant 是否启动：`docker ps | grep qdrant`
2. PostgreSQL 是否连接成功
3. 查看日志：`logs/app-{Date}.log`

### Q2: 检索不到相关记忆？

调整参数：
- 降低 `minScore`（例如从 0.6 → 0.4）
- 增加 `topK`（例如从 5 → 10）
- 检查 `userId` 是否正确

### Q3: 如何删除测试数据？

```bash
curl -X DELETE http://localhost:5000/api/memory/test_user_001
```

## 📚 完整文档

详细设计和高级特性，请参考：
- [LONG_TERM_MEMORY.md](./LONG_TERM_MEMORY.md) - 完整设计文档

## 🚀 下一步

1. ✅ 测试基础 API
2. 🔄 集成到你的对话系统
3. 🎨 根据业务需求调整记忆类型
4. 📈 添加记忆分析和可视化
5. 🛡️ 添加认证和权限控制

---

**关键优势**:
- ✨ 零配置启动（复用现有 Qdrant + PostgreSQL）
- 🎯 精确的用户隔离
- 🧠 智能的语义检索
- 📊 灵活的记忆管理
- 🔌 简单的 HTTP API 集成

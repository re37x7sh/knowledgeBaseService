# 长期记忆功能部署检查清单

## ✅ 已完成的工作

### 📁 代码文件（已创建）

- ✅ `Core/Entities/ConversationMemory.cs` - 记忆实体定义
- ✅ `Application/Interfaces/IConversationMemoryService.cs` - 服务接口
- ✅ `Application/Services/ConversationMemoryService.cs` - 核心服务实现（330行）
- ✅ `Api/Controllers/MemoryController.cs` - RESTful API 控制器

### 📚 文档文件（已创建）

- ✅ `docs/MEMORY_README.md` - 功能总览
- ✅ `docs/LONG_TERM_MEMORY_QUICKSTART.md` - 快速开始
- ✅ `docs/LONG_TERM_MEMORY.md` - 详细设计
- ✅ `docs/MEMORY_IMPLEMENTATION_COMPARISON.md` - 方案对比
- ✅ `docs/MEMORY_INTEGRATION_EXAMPLES.md` - 集成示例
- ✅ `docs/MEMORY_ARCHITECTURE_DIAGRAMS.md` - 架构图

---

## 🔧 需要手动完成的配置（2步）

### 步骤 1: 注册服务到 DI 容器

**文件**: `KnowledgeBaseService.Api/Program.cs`

在现有的服务注册代码后添加（约 L110）：

```csharp
// 注册长期记忆服务
builder.Services.AddScoped<IConversationMemoryService, ConversationMemoryService>();

_logger.LogInformation("已注册长期记忆服务");
```

**位置参考**: 在 `AddScoped<IRAGService>` 或 `AddScoped<IDocumentService>` 之后

---

### 步骤 2: 初始化 Qdrant Collection

**文件**: `KnowledgeBaseService.Api/ServiceInitializationHostedService.cs`

在 `StartAsync` 方法中，现有的 Qdrant collection 初始化代码之后添加：

```csharp
try
{
    // 初始化长期记忆 collection
    var memoryService = serviceScope.ServiceProvider
        .GetRequiredService<IConversationMemoryService>();
    
    if (memoryService is ConversationMemoryService concreteService)
    {
        await concreteService.InitializeCollectionAsync(cancellationToken);
        _logger.LogInformation("长期记忆 collection 初始化成功");
    }
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "长期记忆 collection 初始化失败（Qdrant 可能未启动）");
}
```

**位置参考**: 在现有的 `CreateCollectionAsync` 调用之后，`StopAsync` 方法之前

---

## 🚀 验证部署

### 1. 编译检查

```bash
cd KnowledgeBaseService.Api
dotnet build
```

**预期输出**: 无错误，无警告

---

### 2. 启动服务

```bash
# 启动 Qdrant（如果还没启动）
cd docker
docker-compose up -d qdrant

# 启动 API
cd ../KnowledgeBaseService.Api
dotnet run
```

**检查日志**:
```
[10:30:45 INF] 已注册长期记忆服务
[10:30:46 INF] 成功创建记忆 collection: conversation_memory_collection
[10:30:46 INF] 长期记忆服务初始化成功
```

---

### 3. 测试 API

#### 测试 1: 保存记忆

```bash
curl -X POST http://localhost:5000/api/memory/save \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "test_user",
    "userMessage": "我是一名软件工程师，专注于后端开发",
    "assistantMessage": "明白了，您是后端工程师。我会为您提供相关建议。",
    "memoryType": "fact",
    "importanceScore": 0.9
  }'
```

**预期响应**:
```json
{
  "memoryId": "xxxx-xxxx-xxxx-xxxx",
  "message": "记忆保存成功"
}
```

---

#### 测试 2: 检索记忆

```bash
curl -X POST http://localhost:5000/api/memory/retrieve \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "test_user",
    "query": "帮我推荐一个后端框架",
    "topK": 5,
    "minScore": 0.6
  }'
```

**预期响应**:
```json
{
  "count": 1,
  "memories": [
    {
      "id": "xxxx-xxxx-xxxx-xxxx",
      "summary": "我是一名软件工程师，专注于后端开发",
      "similarityScore": 0.85,
      "importanceScore": 0.9,
      "memoryType": "fact"
    }
  ]
}
```

---

#### 测试 3: Swagger 文档

访问: http://localhost:5000/swagger

**检查**:
- ✅ 看到 `Memory` 控制器
- ✅ 5个端点都可见：
  - POST `/api/memory/save`
  - POST `/api/memory/retrieve`
  - GET `/api/memory/{userId}/recent`
  - POST `/api/memory/{userId}/cleanup`
  - DELETE `/api/memory/{userId}`

---

## 🗄️ 数据库检查

### PostgreSQL 表验证

```sql
-- 连接到 PostgreSQL
SELECT * FROM "ConversationMemory" LIMIT 5;
```

**预期结果**: 能看到刚保存的记忆记录

---

### Qdrant Collection 验证

访问: http://localhost:6333/dashboard

**检查**:
- ✅ 存在 `conversation_memory_collection`
- ✅ 向量维度: 2560
- ✅ Points 数量 > 0

或使用 API：

```bash
curl http://localhost:6333/collections/conversation_memory_collection
```

---

## 📊 性能测试

### 批量保存测试

```bash
for i in {1..10}; do
  curl -X POST http://localhost:5000/api/memory/save \
    -H "Content-Type: application/json" \
    -d "{
      \"userId\": \"test_user\",
      \"userMessage\": \"测试消息 $i\",
      \"assistantMessage\": \"测试回复 $i\"
    }"
done
```

**预期**: 10条记忆全部保存成功

---

### 检索性能测试

```bash
time curl -X POST http://localhost:5000/api/memory/retrieve \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "test_user",
    "query": "测试",
    "topK": 10
  }'
```

**预期延迟**: < 100ms（本地部署）

---

## 🐛 常见问题排查

### Q1: 编译错误 "找不到 IConversationMemoryService"

**原因**: 文件未正确添加到项目

**解决**:
```bash
cd KnowledgeBaseService.Application
dotnet add reference ../KnowledgeBaseService.Core
dotnet build
```

---

### Q2: 运行时错误 "Unable to resolve service"

**原因**: 忘记在 `Program.cs` 中注册服务

**解决**: 按照"步骤 1"添加服务注册

---

### Q3: Qdrant collection 创建失败

**原因**: Qdrant 未启动

**解决**:
```bash
docker ps | grep qdrant  # 检查是否运行
docker-compose up -d qdrant  # 启动 Qdrant
```

---

### Q4: 检索返回空列表

**可能原因**:
1. `minScore` 设置过高 → 降低到 0.4
2. 查询文本与记忆内容差异大 → 使用更相似的查询
3. `userId` 不匹配 → 检查是否使用了相同的 userId

---

## 📝 可选配置

### 自定义配置参数

在 `appsettings.json` 中添加：

```json
{
  "Memory": {
    "DecayFactorDays": 30,           // 记忆衰减周期（天）
    "MaxMemoriesPerUser": 1000       // 每个用户最大记忆数
  }
}
```

---

### 启用 Redis 缓存（可选）

如果需要更高性能，可以添加 Redis 缓存：

```csharp
// Program.cs
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = configuration["Redis:ConnectionString"];
});
```

---

## ✅ 部署完成标志

当你完成以上所有步骤后，应该能够：

- ✅ 成功启动服务，无错误日志
- ✅ 在 Swagger 中看到 Memory API
- ✅ 成功保存记忆并检索到相关结果
- ✅ PostgreSQL 中有 `ConversationMemory` 表
- ✅ Qdrant 中有 `conversation_memory_collection`

---

## 🎯 下一步

1. **集成到对话应用**: 参考 [MEMORY_INTEGRATION_EXAMPLES.md](./MEMORY_INTEGRATION_EXAMPLES.md)
2. **添加认证**: 保护 Memory API（建议使用 JWT）
3. **监控和日志**: 添加记忆访问统计
4. **定期清理**: 设置后台任务清理过期记忆

---

## 📚 相关文档

- [快速开始指南](./LONG_TERM_MEMORY_QUICKSTART.md)
- [详细架构设计](./LONG_TERM_MEMORY.md)
- [集成示例代码](./MEMORY_INTEGRATION_EXAMPLES.md)
- [架构可视化图](./MEMORY_ARCHITECTURE_DIAGRAMS.md)

---

**注意**: 如果遇到任何问题，请检查日志文件 `logs/app-{Date}.log` 获取详细错误信息。

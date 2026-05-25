# 架构和设计决策

## 核心架构原则

### 1. 4层清晰架构

```
┌─────────────────────────────────────────────────────┐
│           API 层 (Web API)                          │
│  - 控制器 (Controllers)                             │
│  - 中间件 (Middleware)                              │
│  - 依赖注入配置                                     │
└──────────────────┬──────────────────────────────────┘
                   │ 依赖
┌──────────────────▼──────────────────────────────────┐
│        Application 层 (业务逻辑)                   │
│  - RAGService (核心 RAG 逻辑)                      │
│  - DocumentService (文档管理)                       │
│  - DTO (数据传输对象)                              │
└──────────────────┬──────────────────────────────────┘
                   │ 依赖
┌──────────────────▼──────────────────────────────────┐
│    Infrastructure 层 (基础设施/外部集成)            │
│  - DeepSeekEmbeddingClient (向量化)               │
│  - DeepSeekChatClient (LLM)                        │
│  - QdrantHttpClient (向量数据库)                    │
└──────────────────┬──────────────────────────────────┘
                   │ 依赖
┌──────────────────▼──────────────────────────────────┐
│         Core 层 (领域模型)                         │
│  - Entities (实体)                                 │
│  - Constants (常量)                                │
│  - ValueObjects (值对象)                           │
└─────────────────────────────────────────────────────┘
```

**优势**:
- 清晰的职责划分
- 易于测试和维护
- 技术决策隔离在 Infrastructure 层
- 业务逻辑不依赖框架

### 2. 为什么不使用 Semantic Kernel

| 方面 | Semantic Kernel | 直接 HttpClient |
|------|------------------|-----------------|
| 学习曲线 | 陡峭 | 平缓 |
| 代码复杂度 | 高（需要配置） | 低（直接） |
| 依赖数量 | 多 | 少 |
| RAG 流程 | 需要适配器 | 直接实现 |
| 调试难度 | 高 | 低 |
| 定制化程度 | 受限 | 完全 |

**我们的选择**:
- RAG 流程简单直接（向量化 → 搜索 → 提示词 → LLM）
- 直接 HttpClient 更轻量、更易维护
- 完全控制每一步流程
- 错误排查更容易

### 3. API-First 设计

所有外部调用都通过 HTTP REST API:

```
应用 → HttpClient → DeepSeek API ✓
应用 → HttpClient → Qdrant API ✓
应用 ✗ 本地 GPU（不使用）
应用 ✗ SDK 依赖（不使用）
```

**优势**:
- 无需本地计算资源
- 成本效益高
- 易于扩展
- 云原生设计

### 4. Docker Compose 部署

```yaml
qdrant → 向量数据库
redis  → 缓存服务
api    → ASP.NET Core 应用
```

**为什么不用 Kubernetes**:
- 项目规模小
- K8s 学习成本高
- Docker Compose 足够满足需求
- 部署和维护更简单

## RAG 实现流程

### 标准 RAG 流程（4步）

```
用户问题
    │
    ├─→ [Step 1] 向量化
    │         调用 DeepSeek Embedding API
    │         返回 1536 维向量
    │
    ├─→ [Step 2] 相似性搜索
    │         在 Qdrant 中搜索
    │         返回 Top-K 相关文档
    │
    ├─→ [Step 3] 提示词构建
    │         组织系统提示词
    │         整合相关文档内容
    │         添加用户问题
    │
    └─→ [Step 4] LLM 生成
              调用 DeepSeek Chat API
              返回上下文相关的答案
```

### 关键实现细节

#### Step 1: 向量化

```csharp
// DeepSeekEmbeddingClient.cs
public async Task<EmbeddingResult> GetEmbeddingAsync(string text)
{
    // 请求格式
    var request = new {
        model = "deepseek-embedding",
        input = text,
        encoding_format = "float"
    };
    
    // 返回 1536 维向量
    // 每个维度是 float 类型
}
```

**特点**:
- 模型: `deepseek-embedding`
- 维度: 1536
- 格式: Float 数组
- 用途: 用于相似性计算

#### Step 2: 向量搜索

```csharp
// QdrantHttpClient.cs - 余弦相似度搜索
public async Task<List<(ulong, float, Dictionary)>> SearchAsync(
    string collectionName, 
    float[] vector, 
    int topK = 5,
    float scoreThreshold = 0.5f)
{
    // 使用余弦相似度度量
    // 返回相似度分数最高的 topK 文档
    // 过滤低于阈值的结果
}
```

**特点**:
- 距离度量: 余弦相似度
- 相似度范围: 0-1
- 支持阈值过滤
- 支持动态 topK

#### Step 3: 提示词工程

```csharp
// RAGService.cs
var systemPrompt = @"你是一个知识库助手。
请根据提供的文档内容准确地回答用户的问题。
如果文档中没有相关信息，请明确说明。
回答应该简洁且信息丰富。";

var contextBuilder = new StringBuilder();
contextBuilder.AppendLine("基于以下相关文档，请回答用户的问题:\n");

// 添加检索到的文档
foreach (var doc in searchResults)
{
    contextBuilder.AppendLine($"【{doc.Title}】(相关度: {doc.Score:F2})");
    contextBuilder.AppendLine(doc.Content);
}

// 添加用户问题
contextBuilder.AppendLine($"\n用户问题: {question}");
```

**优化策略**:
- 明确指示 LLM 基于文档回答
- 包含相关度分数
- 限制文档长度
- 清晰的格式化

#### Step 4: LLM 生成

```csharp
// DeepSeekChatClient.cs
var messages = new List<ChatMessage>
{
    new(MessageRole.System, systemPrompt),
    new(MessageRole.User, builtContext + question)
};

var answer = await _chatClient.GetCompletionAsync(
    messages,
    temperature: 0.7,      // 创意度
    maxTokens: 1024        // 响应长度限制
);
```

**参数说明**:
- `temperature`: 0-2，数值越高越创意（0.7 推荐）
- `maxTokens`: 限制响应长度
- `messages`: 包含系统提示和上下文

## 核心服务设计

### RAGService

**职责**: 编排 RAG 的 4 个步骤

**关键方法**:
```csharp
// 单次查询
public async Task<RAGQueryResponse> QueryAsync(
    RAGQueryRequest request)

// 流式查询
public IAsyncEnumerable<string> QueryStreamAsync(
    RAGQueryRequest request)

// 索引文档
public async Task<bool> IndexDocumentAsync(
    string documentId, 
    string content, 
    Dictionary<string, object> metadata)
```

**生命周期**:
1. 应用启动 → ServiceInitializationHostedService 初始化 Qdrant 集合
2. 创建文档 → 触发异步索引
3. 索引文档 → 向量化 + 上传到 Qdrant
4. 执行查询 → 完整 RAG 流程
5. 返回结果 → 包含来源文档和统计信息

### DocumentService

**职责**: 文档的 CRUD 操作

**存储方式**: 内存 Dictionary（当前）
- 适合演示和开发
- 可轻松替换为数据库

**扩展建议**:
```csharp
// 可实现 IDocumentRepository
public interface IDocumentRepository
{
    Task CreateAsync(Document doc);
    Task<Document> GetAsync(string id);
    Task UpdateAsync(Document doc);
    Task DeleteAsync(string id);
}

// 具体实现可选
// - EntityFramework（SQL Server/PostgreSQL）
// - MongoDB
// - CosmosDB
```

## 错误处理策略

### 异常分类

```
┌─ ValidationException ─→ 400 Bad Request
│
├─ NotFoundException ───→ 404 Not Found
│
├─ InvalidOperationException ─→ 500 Internal Error
│
└─ Exception (其他) ───→ 500 Internal Error
```

### 日志记录

```csharp
// INFO: 正常操作流程
_logger.LogInformation("RAG query: {Question}", request.Question);

// WARNING: 验证失败
_logger.LogWarning("Invalid input: {Error}", ex.Message);

// ERROR: 异常情况
_logger.LogError(ex, "Failed to execute RAG query");
```

## 性能考虑

### 缓存策略

**建议实现**:
```csharp
// 缓存相同查询的向量
public async Task<EmbeddingResult> GetEmbeddingAsync(string text)
{
    var cacheKey = $"embedding:{SHA256(text)}";
    if (_cache.TryGetValue(cacheKey, out var cached))
        return cached;
    
    var result = await _deepSeek.GetEmbeddingAsync(text);
    _cache.Set(cacheKey, result, TimeSpan.FromHours(24));
    return result;
}
```

### 连接池

```csharp
// HttpClientFactory 自动管理连接池
builder.Services
    .AddHttpClient<IDeepSeekEmbeddingClient, DeepSeekEmbeddingClient>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));
```

### 异步优先

所有 I/O 操作都使用 async/await:
- ✓ 数据库查询
- ✓ HTTP 调用
- ✓ 文件操作

## 安全性

### API Key 管理

```csharp
// ✓ 正确做法
var apiKey = configuration["DeepSeek:ApiKey"];

// ✗ 错误做法
var apiKey = "sk-xxx"; // 硬编码
```

### 输入验证

```csharp
// 验证用户输入
if (string.IsNullOrWhiteSpace(request.Question))
    throw new ArgumentException("Question cannot be empty");

// 限制查询大小
if (request.Question.Length > 10000)
    throw new ArgumentException("Question too long");
```

## 监控和调试

### Swagger/OpenAPI

所有端点都有 Swagger 文档:
```
http://localhost:5000/swagger
```

### 健康检查

```csharp
app.MapGet("/health", () => 
    Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));
```

### 结构化日志

```csharp
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
```

---

**设计原则总结**:
1. 简单 > 复杂
2. 透明 > 黑盒
3. 可控 > 自动
4. 明确 > 隐含

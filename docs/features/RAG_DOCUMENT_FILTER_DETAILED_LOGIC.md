# 🎯 RAG 文档过滤方案一 - 详细逻辑说明

## 核心工作原理

### 两种模式对比

```
┌─────────────────────────────────────────────────────────────┐
│ 模式一：已选择文档                                           │
├─────────────────────────────────────────────────────────────┤
│ 用户操作：在文档列表中选择 1 个或多个文档                   │
│ 进入 RAG 对话：带上选中的文档 ID                             │
│ 发送请求：{                                                 │
│     question: "请问这个文件讲了什么？",                      │
│     documentIds: ["doc-123", "doc-456"]  ← 有选择           │
│ }                                                            │
│ 后端处理：                                                   │
│   ✓ 构建 filter: document_id IN ("doc-123", "doc-456")      │
│   ✓ 只在这 2 个文档中搜索                                    │
│   ✓ 向量搜索范围缩小 → 性能更快                              │
│ 结果：针对特定文档的精确对话                                │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ 模式二：未选择文档（全库模式）                              │
├─────────────────────────────────────────────────────────────┤
│ 用户操作：直接进入 RAG 对话（不选文档）                     │
│ 发送请求：{                                                 │
│     question: "系统中有哪些关于 AI 的内容？",               │
│     documentIds: null  ← 无选择 或 documentIds: []          │
│ }                                                            │
│ 后端处理：                                                   │
│   ✓ 检查 documentIds 是否为 null/empty                      │
│   ✓ 如果为空，filter = null（无过滤）                       │
│   ✓ 在全库所有文档中搜索                                    │
│   ✓ 向量搜索范围 = 全库                                     │
│ 结果：全库范围的综合对话                                    │
└─────────────────────────────────────────────────────────────┘
```

---

## 代码实现细节

### Backend 逻辑

#### 1. 请求 DTO 定义

```csharp
public class RAGQueryRequest
{
    public string Question { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
    public float ScoreThreshold { get; set; } = 0.5f;
    public float Temperature { get; set; } = 0.7f;
    public int MaxTokens { get; set; } = 1024;
    
    // 新增：文档 ID 列表（可选）
    public List<string>? DocumentIds { get; set; }
}
```

#### 2. Filter 构建逻辑

```csharp
private Filter? BuildDocumentFilter(List<string>? documentIds)
{
    // 🔑 关键判断：如果 documentIds 为 null 或空，返回 null
    if (documentIds == null || documentIds.Count == 0)
    {
        _logger.LogInformation("No document filter - searching all documents");
        return null;  // ← 返回 null 意味着"不过滤"，即全库搜索
    }

    // 有文档 ID 的情况
    _logger.LogInformation("Filtering documents: {DocumentCount}", documentIds.Count);
    
    if (documentIds.Count == 1)
    {
        // 单个文档：精确匹配
        return new Filter
        {
            Must = new List<Condition>
            {
                new()
                {
                    Key = "document_id",
                    Match = new MatchValue { Value = documentIds[0] }
                }
            }
        };
    }

    // 多个文档：OR 条件（任意一个匹配）
    var conditions = documentIds.Select(docId => new Condition
    {
        Key = "document_id",
        Match = new MatchValue { Value = docId }
    }).ToList();

    return new Filter
    {
        Should = conditions  // OR 关系：文档 ID = doc1 OR doc2 OR doc3
    };
}
```

#### 3. 搜索执行

```csharp
public async Task<RAGQueryResponse> QueryAsync(
    RAGQueryRequest request, 
    CancellationToken cancellationToken = default)
{
    // ... 前置步骤 ...

    // 关键步骤：构建 filter
    var filter = BuildDocumentFilter(request.DocumentIds);
    
    // 执行搜索 - filter 为 null 时不过滤
    var searchResults = await _qdrantClient.SearchAsync(
        CollectionName,
        questionVector,
        topK: Math.Min(request.TopK, QdrantConstants.MaxTopK),
        scoreThreshold: 0.3f,
        filter: filter,  // ← 如果为 null，Qdrant 搜索全库
        cancellationToken: cancellationToken);

    // 日志记录
    if (filter == null)
    {
        _logger.LogInformation("Global search: Found {Count} results from entire knowledge base", 
            searchResults.Count);
    }
    else
    {
        _logger.LogInformation("Filtered search: Found {Count} results from specified documents", 
            searchResults.Count);
    }

    // ... 后续处理 ...
}
```

---

## 前端调用方式

### 场景 1：全库对话（无文档选择）

```typescript
// 用户进入 RAG 对话界面，没选择任何文档
const askQuestion = async (question: string) => {
  const response = await ragApi.query({
    question: question,
    // ❌ 不传 documentIds，或传 null/[]
    // documentIds: undefined  ← 默认值
  });
  
  // 后端会进行全库搜索
  console.log("搜索范围：全库所有文档");
}

// 调用示例
await askQuestion("系统中有关于数据库的内容吗？");
```

### 场景 2：特定文档对话（已选择）

```typescript
// 用户选择了 2 个文档后进入对话
const askQuestionForSelectedDocs = async (
  question: string, 
  selectedDocIds: string[]
) => {
  const response = await ragApi.query({
    question: question,
    documentIds: selectedDocIds  // ✅ 明确传入
  });
  
  // 后端会只在这些文档中搜索
  console.log(`搜索范围：${selectedDocIds.length} 个选中文档`);
}

// 调用示例
await askQuestionForSelectedDocs(
  "这个文档讲了什么？",
  ["doc-123", "doc-456"]
);
```

---

## 执行流程图

### 全库对话流程

```
┌─ 用户进入 RAG 对话 ─┐
│                    │
│ 未选择任何文档    │
│                    │
└─────────┬──────────┘
          │
          ↓
┌─ 构建请求 ─────────────────────────────┐
│ RAGQueryRequest {                       │
│   question: "...",                      │
│   documentIds: null  ← 关键：无文档过滤 │
│ }                                       │
└─────────┬──────────────────────────────┘
          │
          ↓
┌─ 后端处理 ─────────────────────────────┐
│ filter = BuildDocumentFilter(null)      │
│ → 检查：null || count==0 ? return null  │
│ → 结果：filter = null                   │
└─────────┬──────────────────────────────┘
          │
          ↓
┌─ Qdrant 向量搜索 ──────────────────────┐
│ SearchAsync(                            │
│   collection: "documents",              │
│   vector: [问题向量],                   │
│   filter: null  ← 无过滤条件            │
│ )                                       │
│ → 搜索范围：ALL DOCUMENTS               │
│ → 返回：全库中相关度最高的 Top K 结果  │
└─────────┬──────────────────────────────┘
          │
          ↓
┌─ 生成答案 ─────────────────────────────┐
│ LLM 基于全库文档内容生成回答            │
│ 答案涵盖：整个知识库的信息              │
└─────────┬──────────────────────────────┘
          │
          ↓
    返回用户
```

### 特定文档对话流程

```
┌─ 用户选择文档 ──────────────┐
│ [✓] 技术方案.pdf (doc-1)   │
│ [✓] 实现指南.md (doc-2)    │
│ [ ] 其他文档               │
└─────────┬──────────────────┘
          │
          ↓
┌─ 构建请求 ────────────────────────────────┐
│ RAGQueryRequest {                          │
│   question: "...",                         │
│   documentIds: ["doc-1", "doc-2"]          │
│ }   ← 明确指定要搜索的文档                 │
└─────────┬────────────────────────────────┘
          │
          ↓
┌─ 后端处理 ────────────────────────────────┐
│ filter = BuildDocumentFilter(["doc-1", ...])
│ → 检查：count > 0 ? build filter          │
│ → 构建 Filter:                            │
│   {                                       │
│     should: [                             │
│       {key: "document_id", match: "doc-1"}│
│       {key: "document_id", match: "doc-2"}│
│     ]                                     │
│   }                                       │
└─────────┬────────────────────────────────┘
          │
          ↓
┌─ Qdrant 向量搜索 ────────────────────────┐
│ SearchAsync(                              │
│   collection: "documents",                │
│   vector: [问题向量],                     │
│   filter: {should: [...]}  ← 有过滤      │
│ )                                         │
│ → 搜索范围：ONLY 2 documents              │
│ → 返回：这 2 个文档中相关度最高的 Top K  │
└─────────┬────────────────────────────────┘
          │
          ↓
┌─ 生成答案 ────────────────────────────────┐
│ LLM 基于这 2 个文档内容生成回答           │
│ 答案只涉及：这 2 个选中文档的信息         │
└─────────┬────────────────────────────────┘
          │
          ↓
    返回用户
```

---

## 关键代码注释

### RAGService.cs 关键部分

```csharp
/// <summary>
/// 执行 RAG 查询
/// </summary>
public async Task<RAGQueryResponse> QueryAsync(
    RAGQueryRequest request, 
    CancellationToken cancellationToken = default)
{
    try
    {
        // ... 验证 ...

        // 📌 第1步：向量化问题
        var embeddingResult = await _embeddingClient.GetEmbeddingAsync(
            request.Question, 
            cancellationToken);

        // 📌 第2步：构建过滤器（关键！）
        // 根据 documentIds 决定搜索范围
        var filter = BuildDocumentFilter(request.DocumentIds);
        
        // 记录搜索范围
        var searchScope = filter == null ? "全库" : $"{request.DocumentIds?.Count} 个文档";
        _logger.LogInformation("搜索范围：{Scope}", searchScope);

        // 📌 第3步：向量搜索（使用 filter）
        var searchResults = await _qdrantClient.SearchAsync(
            CollectionName,
            embeddingResult.Vector,
            topK: request.TopK,
            scoreThreshold: request.ScoreThreshold,
            filter: filter,  // ← 核心：传入 filter
            cancellationToken: cancellationToken);

        // 📌 第4步：生成答案
        // ... 构建上下文、调用 LLM ...

        return response;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "RAG 查询失败");
        throw;
    }
}

/// <summary>
/// 构建 Qdrant 过滤条件
/// 
/// ✅ 返回 null   → 全库搜索（无文档限制）
/// ✅ 返回 Filter → 仅搜索指定文档
/// </summary>
private Filter? BuildDocumentFilter(List<string>? documentIds)
{
    // 🎯 关键判断：
    // 如果 documentIds 为 null 或空，意味着用户未选择任何文档
    // → 进行全库搜索
    if (documentIds == null || documentIds.Count == 0)
    {
        _logger.LogInformation("无文档过滤 → 执行全库搜索");
        return null;
    }

    // 有文档 ID 的情况
    _logger.LogInformation("添加文档过滤：{DocumentCount} 个", documentIds.Count);

    if (documentIds.Count == 1)
    {
        return new Filter
        {
            Must = new List<Condition>
            {
                new()
                {
                    Key = "document_id",
                    Match = new MatchValue { Value = documentIds[0] }
                }
            }
        };
    }

    // 多个文档：使用 OR 条件
    var conditions = documentIds
        .Select(docId => new Condition
        {
            Key = "document_id",
            Match = new MatchValue { Value = docId }
        })
        .ToList();

    return new Filter { Should = conditions };
}
```

---

## 实际使用示例

### 示例 1：全库查询

**请求**：
```json
POST /api/rag/query
{
  "question": "系统中有关于 ML 的内容吗？",
  "topK": 5
  // 注意：没有 documentIds 字段，或 documentIds: null
}
```

**后端处理**：
```
1. filter = BuildDocumentFilter(null) → 返回 null
2. Qdrant 搜索全库文档
3. 返回全库中最相关的 5 个片段
4. LLM 基于全库内容生成答案
```

**响应**：
```json
{
  "question": "系统中有关于 ML 的内容吗？",
  "answer": "是的，系统中有关于机器学习的内容...",
  "sources": [
    { "documentId": "doc-123", "title": "深度学习指南", ... },
    { "documentId": "doc-456", "title": "NLP 入门", ... },
    ...
  ]
}
```

### 示例 2：特定文档查询

**请求**：
```json
POST /api/rag/query
{
  "question": "这个文件的主要内容是什么？",
  "topK": 5,
  "documentIds": ["doc-123"]
}
```

**后端处理**：
```
1. filter = BuildDocumentFilter(["doc-123"]) → 返回 Filter
2. Qdrant 仅在 doc-123 中搜索
3. 返回该文档中最相关的 5 个片段
4. LLM 基于该文档内容生成答案
```

**响应**：
```json
{
  "question": "这个文件的主要内容是什么？",
  "answer": "这个文件主要介绍了...",
  "sources": [
    { "documentId": "doc-123", "title": "...", ... }
  ]
}
```

### 示例 3：多文档查询

**请求**：
```json
POST /api/rag/query
{
  "question": "比较这两个文件的观点",
  "topK": 10,
  "documentIds": ["doc-111", "doc-222"]
}
```

**后端处理**：
```
1. filter = BuildDocumentFilter(["doc-111", "doc-222"]) 
   → 返回 Filter with OR 条件
2. Qdrant 在 doc-111 或 doc-222 中搜索
3. 返回这两个文档中最相关的 10 个片段
4. LLM 基于这两个文档的内容对比回答
```

---

## 关键特性总结

| 特性 | 实现方式 | 工作原理 |
|------|---------|---------|
| **全库搜索** | `documentIds = null` | Qdrant 不应用过滤条件，搜索所有向量 |
| **单文档搜索** | `documentIds = ["doc-1"]` | Qdrant 过滤 document_id = "doc-1" |
| **多文档搜索** | `documentIds = ["doc-1", "doc-2"]` | Qdrant 过滤 (document_id = "doc-1" OR "doc-2") |
| **动态切换** | 用户操作 + 前端传参 | 无需重启，实时切换 |
| **性能优化** | 数据库层过滤 | 搜索空间缩小 → 更快 |

---

## 总结

✅ **方案一完整解决方案**：

1. **全库对话**（默认模式）
   - 不传 `documentIds` 或传 `null`
   - 后端自动进行全库搜索
   - 适合：综合查询、跨文档问答

2. **特定文档对话**（选择模式）
   - 传入 `documentIds` 列表
   - 后端仅在这些文档中搜索
   - 适合：文档聚焦查询、精确问答

3. **动态切换**
   - 用户可在任何时刻改变选择
   - 前端动态调整 `documentIds` 参数
   - 后端自动处理

**这就是方案一的完整工作流程！** 🚀

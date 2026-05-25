# 版本管理与 RAG 检索集成说明

## 🎯 核心问题

**问题**：创建新版本后，RAG 检索无法检索到新版本的内容。

**原因**：之前的版本管理系统只保存了文档历史版本到数据库，但没有同步更新 Qdrant 向量数据库，导致 RAG 检索仍然使用旧版本的内容。

## ✅ 解决方案

已实现完整的**版本管理 + 向量数据库同步**机制：

### 1️⃣ 创建新版本时
```
用户创建新版本
    ↓
保存版本到数据库 (DocumentVersion)
    ↓
更新主文档内容 (Document.Content, Document.Title)
    ↓
【异步】重新索引到 Qdrant 向量数据库
    - 使用新版本的内容
    - 分块、向量化
    - 更新元数据（版本号、标题、分类）
```

### 2️⃣ 回滚版本时
```
用户回滚到指定版本
    ↓
创建新的回滚版本记录
    ↓
更新主文档内容为目标版本的内容
    ↓
【异步】重新索引到 Qdrant 向量数据库
    - 使用回滚后的内容
    - 完整重建向量索引
```

### 3️⃣ 删除文档时
```
用户删除文档
    ↓
标记文档为已删除 (Document.IsDeleted = true)
    ↓
【新增】删除 Qdrant 中该文档的所有向量点
```

## 🔧 技术实现

### 新增接口和方法

#### IQdrantHttpClient
```csharp
/// <summary>
/// 删除文档的所有向量点（按 document_id 过滤）
/// </summary>
Task<bool> DeletePointsByDocumentIdAsync(
    string collectionName, 
    string documentId, 
    CancellationToken cancellationToken = default);
```

#### DocumentVersionService 修改
- 注入 `IDocumentRepository` 和 `IRAGService`
- `CreateVersionAsync()` 方法：
  - 更新主文档内容
  - 异步重新索引到 Qdrant
- `RollbackToVersionAsync()` 方法：
  - 更新主文档内容为回滚版本
  - 异步重新索引到 Qdrant

### 工作流程

```
DocumentVersionService
    ├─ CreateVersionAsync()
    │   ├─ 保存版本到数据库 ✅
    │   ├─ 更新 Document.Content ✅
    │   └─ 异步调用 RAGService.IndexDocumentAsync() ✅
    │       └─ 分块 → 向量化 → Upsert 到 Qdrant
    │
    └─ RollbackToVersionAsync()
        ├─ 创建回滚版本记录 ✅
        ├─ 更新 Document.Content ✅
        └─ 异步调用 RAGService.IndexDocumentAsync() ✅
```

## 📊 数据流

```
用户操作          数据库层                    向量数据库
   │                │                          │
   ├─创建版本───→ DocumentVersion.Insert      │
   │                │                          │
   │             Document.Content 更新         │
   │                │                          │
   │                └────────────────────→ Qdrant.Upsert
   │                                           (分块 + 向量化)
   │
   ├─RAG 查询────────────────────────────→ Qdrant.Search
   │                                           ↓
   │                                      返回最新内容
   │                                           ↓
   └─────────────────────────────────────← DeepSeek 生成答案
```

## 🎯 使用场景

### 场景 1：文档内容更新
```bash
# 1. 导入文档
POST /api/documents/import
# → 创建初始版本（version 1）
# → 自动索引到 Qdrant

# 2. 创建新版本（修改内容）
POST /api/documentversions/create
{
  "documentId": "xxx",
  "title": "更新后的标题",
  "content": "更新后的内容...",
  "changeLog": "修复了错别字"
}
# → 创建 version 2
# → 更新主文档内容
# → 重新索引到 Qdrant ✅

# 3. RAG 查询
POST /api/rag/query
{
  "query": "文档内容是什么？"
}
# → 从 Qdrant 检索到最新内容（version 2）✅
# → 返回更新后的答案 ✅
```

### 场景 2：版本回滚
```bash
# 1. 当前是 version 3，想回滚到 version 1
POST /api/documentversions/document/{documentId}/rollback?targetVersion=1
# → 创建 version 4（内容与 version 1 相同）
# → 更新主文档内容为 version 1 的内容
# → 重新索引到 Qdrant ✅

# 2. RAG 查询
POST /api/rag/query
{
  "query": "文档内容是什么？"
}
# → 从 Qdrant 检索到回滚后的内容 ✅
# → 返回 version 1 的内容 ✅
```

## 🔍 验证方法

### 1. 检查主文档内容是否更新
```sql
SELECT Id, Title, Content, UpdatedAt 
FROM Documents 
WHERE Id = 'your-document-id';
```

### 2. 检查版本记录
```sql
SELECT VersionNumber, Title, IsCurrent, CreatedAt 
FROM DocumentVersions 
WHERE DocumentId = 'your-document-id' 
ORDER BY VersionNumber DESC;
```

### 3. 测试 RAG 查询
```bash
# 创建新版本后
curl -X POST "http://localhost:5000/api/documentversions/create" \
  -H "Content-Type: application/json" \
  -d '{
    "documentId": "xxx",
    "title": "测试版本",
    "content": "这是新版本的内容，关键词：蓝色星球",
    "changeLog": "添加了关键词"
  }'

# 等待几秒后（异步索引完成）
curl -X POST "http://localhost:5000/api/rag/query" \
  -H "Content-Type: application/json" \
  -d '{
    "query": "蓝色星球是什么？",
    "useKnowledgeBase": true
  }'

# 应该能检索到新版本的内容 ✅
```

## ⚡ 性能优化

### 异步重新索引
- 使用 `Task.Run()` 异步执行重新索引
- 不阻塞版本创建/回滚操作的返回
- 后台自动完成向量数据库更新

### 日志追踪
```csharp
_logger.LogInformation("Re-indexed document {DocumentId} version {Version} to vector database", 
    documentId, versionNumber);
```

可以通过日志确认重新索引是否成功。

## 🚨 注意事项

1. **异步索引延迟**
   - 创建版本后，向量数据库更新需要几秒钟
   - 如果立即查询，可能还是旧内容
   - 建议等待 3-5 秒后再测试 RAG 查询

2. **大文档处理**
   - 大文档重新索引可能需要更长时间
   - 分块、向量化、上传都是异步的
   - 不会影响用户体验

3. **错误处理**
   - 即使重新索引失败，版本也会成功创建
   - 可以通过日志查看失败原因
   - 需要时可以手动触发重新索引

## 📝 API 端点

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/documentversions/create` | POST | 创建新版本（自动重新索引）✅ |
| `/api/documentversions/document/{id}/rollback` | POST | 回滚版本（自动重新索引）✅ |
| `/api/documentversions/document/{id}` | GET | 获取版本列表 |
| `/api/documentversions/{id}/content` | GET | 获取版本内容 |
| `/api/rag/query` | POST | RAG 查询（使用最新内容）✅ |

## ✅ 总结

版本管理系统现在已经**完全集成**到 RAG 检索流程中：

- ✅ 创建新版本 → 自动更新主文档 → 自动重新索引
- ✅ 回滚版本 → 自动更新主文档 → 自动重新索引
- ✅ RAG 查询 → 始终检索到最新内容
- ✅ 异步处理 → 不影响用户操作响应速度
- ✅ 完整日志 → 可追踪索引状态

**现在，版本管理不仅仅是历史记录，而是真正影响 RAG 检索结果的核心功能！** 🎉

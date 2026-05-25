# 📊 RAG 对话后端服务接口改进方案

## 现有状态分析

### 当前实现
✅ **现有系统已支持基础的 RAG 查询**

**当前流程**：
1. 用户提问 → 向量化问题 → 搜索所有文档 → LLM 生成答案
2. 向量搜索范围：**全库文档**（无文档过滤）
3. 搜索结果：基于相关度评分排序（Top K）

**现有代码证据**：
```csharp
// RAGService.cs - 第2步：向量搜索
var searchResults = await _qdrantClient.SearchAsync(
    CollectionName,
    questionVector,
    topK: Math.Min(request.TopK, QdrantConstants.MaxTopK),
    scoreThreshold: 0.3f,  // 全库搜索
    cancellationToken: cancellationToken);
```

---

## 业务需求分析

### 需求描述
> 可以做到只针对选择的文档进行对话，如果没选择，则支持针对所有文档对话

### 需求拆解

| 场景 | 行为 | 搜索范围 |
|------|------|---------|
| 用户选择文档 | 只搜索该文档 | 单个文档 ID |
| 用户选择多个文档 | 搜索这些文档 | 多个文档 ID |
| 用户未选择文档 | 搜索全库 | 所有文档 |
| 清除选择 | 回到全库搜索 | 所有文档 |

---

## ✅ 可行性分析

### 技术可行性：**100% 可行**

#### 方案一：Filter 方式（推荐 ⭐⭐⭐⭐⭐）

**原理**：Qdrant 支持 payload filter，可在搜索时过滤特定文档

**Qdrant Filter 语法**：
```json
{
  "filter": {
    "must": [
      {
        "key": "document_id",
        "match": {
          "value": "doc-123"
        }
      }
    ]
  }
}
```

**优势**：
- ✅ 向量数据库原生支持
- ✅ 性能最优（数据库层面过滤）
- ✅ 支持多个文档 ID 过滤
- ✅ 无额外计算开销

**实现复杂度**：**简单** (只需修改 Qdrant 查询参数)

#### 方案二：后处理方式

**原理**：搜索所有文档，然后在应用层过滤结果

**优势**：
- ✅ 实现简单
- ✅ 不依赖 Qdrant 版本

**劣势**：
- ❌ 性能较差（返回全量结果再过滤）
- ❌ 网络开销大
- ❌ 相关度排序可能不准确

**实现复杂度**：**更简单，但性能差**

---

## 🎯 实现建议

### 推荐方案：方案一 + Filter 方式

#### Step 1: 修改 RAGQueryRequest DTO

```csharp
public class RAGQueryRequest
{
    /// <summary>
    /// 用户问题
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// 搜索结果数量
    /// </summary>
    public int TopK { get; set; } = 5;

    /// <summary>
    /// 相似度阈值
    /// </summary>
    public float ScoreThreshold { get; set; } = 0.5f;

    /// <summary>
    /// 采样温度 (0-2)
    /// </summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// 最大响应token数
    /// </summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>
    /// 限定的文档 ID 列表（为空表示全库搜索）
    /// </summary>
    public List<string>? DocumentIds { get; set; }
}
```

#### Step 2: 修改 RAGService 的搜索逻辑

```csharp
public async Task<RAGQueryResponse> QueryAsync(RAGQueryRequest request, CancellationToken cancellationToken = default)
{
    // ... 前置步骤 ...

    // 第2步：在 Qdrant 中搜索相似文档（带文档 ID 过滤）
    _logger.LogInformation("Step 2: Searching similar documents...");
    
    // 构建 Qdrant filter（如果指定了文档 ID）
    var filter = BuildDocumentFilter(request.DocumentIds);
    
    var searchResults = await _qdrantClient.SearchAsync(
        CollectionName,
        questionVector,
        topK: Math.Min(request.TopK, QdrantConstants.MaxTopK),
        scoreThreshold: 0.3f,
        filter: filter,  // 新增：传入文档过滤器
        cancellationToken: cancellationToken);

    // ... 后续步骤 ...
}

/// <summary>
/// 构建 Qdrant 过滤条件
/// </summary>
private Filter? BuildDocumentFilter(List<string>? documentIds)
{
    if (documentIds == null || documentIds.Count == 0)
    {
        return null;  // 无过滤，全库搜索
    }

    if (documentIds.Count == 1)
    {
        // 单个文档
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
    var conditions = documentIds.Select(docId => new Condition
    {
        Key = "document_id",
        Match = new MatchValue { Value = docId }
    }).ToList();

    return new Filter
    {
        Should = conditions  // OR 关系
    };
}
```

#### Step 3: 更新 API 端点注释

```csharp
[HttpPost("query")]
[ProducesResponseType(typeof(RAGQueryResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<ActionResult<RAGQueryResponse>> Query(
    [FromBody] RAGQueryRequest request,
    CancellationToken cancellationToken)
{
    // ... 现有代码 ...
}

// API 使用示例：
// 1. 全库搜索：POST /api/rag/query { "question": "..." }
// 2. 特定文档：POST /api/rag/query { "question": "...", "documentIds": ["doc-123"] }
// 3. 多个文档：POST /api/rag/query { "question": "...", "documentIds": ["doc-1", "doc-2"] }
```

---

## 📋 实现检查清单

### 后端修改
- [ ] 修改 `RAGQueryRequest.cs` - 添加 `DocumentIds` 字段
- [ ] 修改 `RAGService.cs` - 实现 `BuildDocumentFilter()` 方法
- [ ] 修改 `RAGService.cs` - 传入 filter 参数到 `SearchAsync()`
- [ ] 修改流式查询方法 `QueryStreamAsync()` - 同样逻辑
- [ ] 编译验证 - `dotnet build`

### 前端集成
- [ ] 修改 `RAGQueryRequest` TypeScript 接口 - 添加 `documentIds` 字段
- [ ] 修改 RAG 组件 - 传入当前选择的文档 ID
- [ ] 修改 API 调用 - 包含文档 ID 参数
- [ ] 编译验证 - `npm run build`

### 测试场景
- [ ] 全库搜索：无文档 ID 时能正确搜索全库
- [ ] 单个文档：指定一个文档 ID 时只搜索该文档
- [ ] 多个文档：指定多个文档 ID 时只搜索这些文档
- [ ] 文档不存在：指定不存在的 ID 时返回空结果
- [ ] 性能测试：验证过滤不会增加显著延迟

---

## 🔄 用户工作流程

### 场景 1：全库对话（未选择文档）
```
用户进入 RAG 对话界面
  ↓
问"数据库有哪些内容？"
  ↓
后端：documentIds = null → 全库搜索
  ↓
从所有文档中找相关内容
  ↓
生成答案
```

### 场景 2：特定文档对话（已选择文档）
```
用户在文档列表中选择"技术文档.pdf"（doc-123）
  ↓
切换到 RAG 对话
  ↓
问"这份文档讲了什么？"
  ↓
后端：documentIds = ["doc-123"] → 仅搜索该文档
  ↓
只从该文档中找内容
  ↓
生成答案
```

### 场景 3：多个文档对话
```
用户在文档列表中多选 3 个文档
  ↓
进入 RAG 对话
  ↓
问相关问题
  ↓
后端：documentIds = ["doc-1", "doc-2", "doc-3"] → 仅搜索这 3 个
  ↓
只从这些文档中找内容
  ↓
生成答案
```

---

## 💰 成本-收益分析

| 维度 | 评分 | 说明 |
|------|------|------|
| 实现成本 | ⭐⭐ | 修改 2-3 个文件，改动量小 |
| 性能收益 | ⭐⭐⭐⭐⭐ | Qdrant 原生过滤，性能优异 |
| 业务价值 | ⭐⭐⭐⭐⭐ | 支持精确查询，UX 显著提升 |
| 维护难度 | ⭐ | 代码清晰易维护 |
| **综合评分** | **⭐⭐⭐⭐⭐** | **强烈推荐实施** |

---

## 🚀 后续优化建议

### 短期（Phase 1）
1. **实施方案一** - 基础文档过滤
2. **添加 UI 指示** - 显示"正在搜索 N 个文档"
3. **快速切换** - 一键切换全库/特定文档

### 中期（Phase 2）
1. **复合过滤** - 支持按分类、标签过滤
2. **搜索历史** - 保存用户的过滤配置
3. **推荐文档** - 基于问题推荐相关文档

### 长期（Phase 3）
1. **集合管理** - 支持创建文档集合
2. **权限管理** - 不同用户可访问的文档范围
3. **缓存优化** - 热点文档搜索缓存

---

## 📚 参考文献

**Qdrant Filter 文档**：https://qdrant.tech/documentation/concepts/filtering/

**常见 Filter 操作**：
- `must`：所有条件都必须满足（AND）
- `should`：至少一个条件满足（OR）
- `must_not`：条件不能满足（NOT）

---

## ✨ 总结

### 能否实现？
✅ **完全可以实现，且非常简单**

### 实现方式？
使用 Qdrant 的 Filter 机制，在向量搜索时添加文档 ID 过滤条件

### 性能影响？
✅ **零负面影响**，Qdrant 原生支持，性能更优

### 建议？
**立即实施方案一**，预计工作量 1-2 小时内完成

---

## 📞 技术支持

有任何问题或需要帮助实现，请随时告诉我！我可以：

1. 提供完整的代码实现
2. 协助 TypeScript 前端修改
3. 编写相应的测试用例
4. 性能测试和验证

**推荐立即启动此改进！** 🎉

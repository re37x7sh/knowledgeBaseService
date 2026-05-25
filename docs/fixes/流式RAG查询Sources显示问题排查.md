# 流式 RAG 查询 Sources 显示问题排查

## 问题描述

用户反馈：流式 RAG 查询时，相关文档展示为空，sources 数据没有正确显示。

## 排查步骤

### 1. 检查后端代码

**RAGService.cs (QueryStreamAsync 方法)**

✅ **确认**：后端代码正确生成并发送 sources 数据

```csharp
// 第 444-447 行
if (sources.Count > 0)
{
    var sourcesJson = System.Text.Json.JsonSerializer.Serialize(new { sources });
    yield return $"[SOURCES]{sourcesJson}[/SOURCES]";
}
```

**关键点**：
- Sources 在流式开始前就已收集完成
- 使用特殊标记 `[SOURCES]...[/SOURCES]` 包裹 JSON 数据
- 先发送 sources，再流式发送内容

### 2. 检查控制器转发逻辑

**RAGController.cs (QueryStream 方法)**

✅ **确认**：控制器正确识别并转发 sources 消息

```csharp
// 第 87-93 行
if (chunk.StartsWith("[SOURCES]") && chunk.EndsWith("[/SOURCES]"))
{
    var sourcesJson = chunk.Substring(9, chunk.Length - 19);
    _logger.LogInformation("发送 sources 数据，长度: {Length}", sourcesJson.Length);
    var streamData = new { type = "sources", data = sourcesJson };
    var json = JsonSerializer.Serialize(streamData);
    await HttpContext.Response.WriteAsync($"data: {json}\n\n");
}
```

**SSE 消息格式**：
```
data: {"type":"sources","data":"{\"sources\":[...]}"}

data: {"type":"content","data":"回答内容"}

data: {"type":"done","data":""}
```

### 3. 检查前端 API 解析

**rag.ts (queryStream 方法)**

✅ **确认**：前端正确解析 SSE 消息

```typescript
// 第 50-56 行
if (line.startsWith('data: ')) {
  const jsonStr = line.slice(6)
  const data = JSON.parse(jsonStr)
  
  yield {
    type: data.type,
    content: data.data
  }
}
```

### 4. 检查状态管理

**chat.ts (queryStream 方法)**

✅ **确认**：前端正确更新消息的 sources 属性

```typescript
// 第 84-97 行
if (chunk.type === 'sources') {
  try {
    const sourcesData = JSON.parse(chunk.content)
    sources = sourcesData.sources || []
    console.log('Received sources:', sources)
    
    const lastMessage = messages.value[messages.value.length - 1]
    if (lastMessage && lastMessage.role === 'assistant') {
      lastMessage.sources = sources
    }
  } catch (e) {
    console.error('Failed to parse sources:', e)
  }
}
```

### 5. 检查 UI 组件渲染

**RAGChat.vue (消息显示)**

✅ **确认**：UI 组件正确渲染 sources

```vue
<!-- 第 40-65 行 -->
<div v-if="message.sources && message.sources.length > 0" class="sources">
  <el-divider direction="horizontal" />
  <div class="sources-title">📚 相关文档</div>
  <div v-for="(source, index) in message.sources" :key="index" class="source-item">
    <div class="source-header">
      <span class="source-title">{{ source.title }}</span>
      <el-tag v-if="source.fileType === 'image'" type="success" size="small">
        🖼️ 图片
      </el-tag>
      <el-tag size="small">
        {{ (source.score * 100).toFixed(1) }}% 相关
      </el-tag>
    </div>
    
    <!-- 如果是图片，显示图片内容 -->
    <div v-if="source.fileType === 'image' && source.imageBase64" class="source-image-container">
      <img 
        :src="`data:image/jpeg;base64,${source.imageBase64}`" 
        class="source-image"
        :alt="source.title"
      />
    </div>
  </div>
</div>
```

## 诊断措施

### 添加详细日志

**后端日志**（RAGController.cs）：

```csharp
_logger.LogInformation("发送 sources 数据，长度: {Length}", sourcesJson.Length);
```

**前端日志**（chat.ts）：

```typescript
console.log('收到流式数据块:', chunk)
console.log('解析 sources，原始内容:', chunk.content)
console.log('Received sources:', sources)
console.log('sources 数量:', sources.length)
console.log('已更新消息的 sources 属性')
```

### 测试步骤

1. **启动服务**：
   ```bash
   # 启动后端
   cd d:\dev\KnowledgeBaseService
   dotnet run --project KnowledgeBaseService.Api
   
   # 启动前端（新终端）
   cd KnowledgeBaseService.Web
   npm run dev
   ```

2. **上传测试文档**：
   - 上传一个包含文字的图片
   - 或上传一个文本文档

3. **执行流式查询**：
   - 在 RAG Chat 界面输入问题
   - 切换到流式模式
   - 提交查询

4. **检查浏览器控制台**：
   ```
   应该看到：
   ✅ 收到流式数据块: {type: 'sources', content: '{"sources":[...]}'}
   ✅ 解析 sources，原始内容: {"sources":[...]}
   ✅ Received sources: [{documentId: '...', title: '...', ...}]
   ✅ sources 数量: 1
   ✅ 已更新消息的 sources 属性
   
   不应该看到：
   ❌ Failed to parse sources: ...
   ❌ sources 数量: 0
   ```

5. **检查后端日志**：
   ```
   应该看到：
   ✅ [INFO] Stream search found X similar documents
   ✅ [INFO] Stream: Using X high-relevant and Y low-relevant documents
   ✅ [INFO] 已在流式查询中加载图片 Base64，大小: X KB
   ✅ [INFO] 发送 sources 数据，长度: X
   ```

## 可能的问题原因

### A. Sources 列表为空

**症状**：后端日志显示 "发送 sources 数据，长度: 16"（只有 `{"sources":[]}` 的长度）

**原因**：
1. Qdrant 搜索结果为空（没有匹配的文档）
2. 相关度分数都低于阈值（0.3）
3. 指定的 documentIds 过滤导致无结果

**解决方案**：
- 检查 Qdrant 中是否有数据：`docker exec -it qdrant_db curl http://localhost:6333/collections/knowledge_base`
- 查看搜索结果日志，确认相关度分数
- 如果使用文档过滤，确认文档 ID 正确

### B. 图片加载失败

**症状**：sources 有数据，但图片不显示

**原因**：
1. 图片文件路径错误
2. 文件不存在或已被删除
3. Base64 编码失败

**解决方案**：
- 检查 `wwwroot/uploads/images/` 目录中是否有图片
- 查看后端日志中的警告信息
- 确认 Docker volume 挂载正确

### C. 前端解析失败

**症状**：控制台显示 "Failed to parse sources: ..."

**原因**：
1. JSON 格式错误
2. sources 数据被截断
3. 特殊字符未正确转义

**解决方案**：
- 查看浏览器控制台的详细错误信息
- 检查网络面板中 SSE 消息的原始内容
- 确认后端发送的 JSON 格式正确

### D. UI 组件未响应式更新

**症状**：sources 数据已更新，但 UI 不显示

**原因**：
1. Vue 响应式系统未触发更新
2. 消息对象引用未改变
3. 条件渲染判断错误

**解决方案**：
- 使用 Vue Devtools 检查消息对象的 sources 属性
- 确认 `v-if="message.sources && message.sources.length > 0"` 条件满足
- 尝试强制更新：`messages.value = [...messages.value]`

## 数据流追踪

### 完整的数据流

```
1. 用户提交查询
   ↓
2. RAGService.QueryStreamAsync() 执行搜索
   ↓
3. 收集 sources 列表（包含图片 Base64）
   ↓
4. yield return "[SOURCES]{json}[/SOURCES]"
   ↓
5. RAGController 识别特殊标记
   ↓
6. 转换为 SSE 消息：data: {"type":"sources","data":"..."}
   ↓
7. 前端 ragApi.queryStream() 解析 SSE
   ↓
8. yield { type: 'sources', content: '...' }
   ↓
9. chat.ts 接收并解析 JSON
   ↓
10. 更新 lastMessage.sources = sources
   ↓
11. Vue 响应式更新触发
   ↓
12. RAGChat.vue 渲染 sources 列表
   ↓
13. 用户看到相关文档和图片
```

### 关键检查点

| 检查点 | 位置 | 成功标志 | 失败标志 |
|-------|------|---------|---------|
| **搜索结果** | RAGService.cs:308 | `Stream search found X similar documents` (X > 0) | `No documents found` |
| **Sources 收集** | RAGService.cs:445 | `sources.Count > 0` | 空列表 |
| **图片加载** | RAGService.cs:411 | `已在流式查询中加载图片 Base64` | `无法加载图片文件` |
| **后端发送** | RAGController.cs:91 | `发送 sources 数据，长度: X` (X > 20) | 无日志或长度 <= 16 |
| **前端接收** | chat.ts:84 | `收到流式数据块: {type: 'sources'}` | 无日志 |
| **JSON 解析** | chat.ts:87 | `Received sources: [...]` | `Failed to parse sources` |
| **UI 更新** | RAGChat.vue:40 | 看到 "📚 相关文档" | 空白区域 |

## 解决方案总结

### 已实施的改进

1. ✅ **添加详细日志**：
   - 后端：sources 数据长度
   - 前端：每个处理步骤的状态

2. ✅ **错误处理增强**：
   - 捕获 JSON 解析异常
   - 输出原始数据用于调试

3. ✅ **数据流优化**：
   - Sources 在流式开始前完成收集
   - 使用特殊标记明确区分消息类型

### 待验证的场景

- [ ] 无匹配文档时的行为
- [ ] 大量 sources（>10个）的性能
- [ ] 图片文件较大（>5MB）的情况
- [ ] 网络延迟导致的消息乱序

### 性能优化建议

1. **图片压缩**：
   - 导入时自动压缩大图
   - 生成缩略图用于预览

2. **Sources 数量限制**：
   - 只返回前 5 个最相关的文档
   - 提供"加载更多"功能

3. **缓存机制**：
   - 缓存已加载的图片 Base64
   - 避免重复读取文件系统

## 测试检查清单

在确认修复后，请按以下清单测试：

- [ ] **流式查询 - 有匹配文档**
  - [ ] Sources 显示正确数量
  - [ ] 文档标题正确显示
  - [ ] 相关度分数正确显示
  - [ ] 文本摘要正确显示

- [ ] **流式查询 - 包含图片**
  - [ ] 图片标签显示 "🖼️ 图片"
  - [ ] 图片正确加载和显示
  - [ ] 匹配提示文本正确显示

- [ ] **流式查询 - 无匹配文档**
  - [ ] 不显示 sources 区域
  - [ ] 显示通用回答（混合模式）

- [ ] **流式查询 - 文档过滤**
  - [ ] 选择文档后只搜索指定文档
  - [ ] Sources 只包含选定文档

- [ ] **控制台日志验证**
  - [ ] 后端日志完整
  - [ ] 前端日志无错误
  - [ ] 网络面板 SSE 消息格式正确

---

**文档版本**: v1.0.0  
**创建时间**: 2025-01-15  
**最后更新**: 2025-01-15  
**维护者**: Knowledge Base Service Team

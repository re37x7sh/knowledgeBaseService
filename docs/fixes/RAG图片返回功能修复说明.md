# RAG 图片返回功能修复说明

## 📋 问题描述

**原问题**：
1. ✅ 非流式查询 (`/api/rag/query`) - 可以返回图片
2. ❌ 流式查询 (`/api/rag/query-stream`) - 无法返回图片

**根本原因**：
- 流式查询只返回文本内容块（`content`），没有返回来源文档信息（`sources`）
- 前端无法获取 `sources`，因此无法显示图片和来源文档

---

## 🔧 解决方案

### 核心思路
在流式查询开始时，先发送一个特殊的 `sources` 消息，包含所有来源文档的完整信息（包括图片 Base64），然后再流式发送答案内容。

### 消息格式
```typescript
// 1. sources 消息（第一条）
{
  type: "sources",
  data: "{\"sources\": [{\"documentId\":\"...\", \"imageBase64\":\"...\", ...}]}"
}

// 2. content 消息（流式多条）
{
  type: "content",
  data: "这是答案的一部分..."
}

// 3. done 消息（最后一条）
{
  type: "done",
  data: ""
}
```

---

## 📝 实施细节

### 1. 后端修改 - RAGService.cs

#### ① `QueryStreamAsync` 方法增强

**原来的逻辑**：
```csharp
foreach (var (_, score, payload) in resultsToUse)
{
    // 只构建上下文文本，不收集 sources
    contextBuilder.AppendLine($"【{title}】");
    contextBuilder.AppendLine(content);
}
```

**修改后的逻辑**：
```csharp
var sources = new List<SourceReference>();  // 新增：收集 sources

foreach (var (_, score, payload) in resultsToUse)
{
    // 构建上下文文本
    contextBuilder.AppendLine($"【{title}】");
    contextBuilder.AppendLine(content);
    
    var source = new SourceReference { ... };
    
    // 检测图片并加载 Base64
    if (content.Contains("[图片路径:"))
    {
        source.FileType = "image";
        var pathMatch = Regex.Match(content, @"\[图片路径:\s*([^\]]+)\]");
        if (pathMatch.Success)
        {
            var imagePath = pathMatch.Groups[1].Value.Trim();
            var fullPath = Path.Combine("wwwroot", imagePath.TrimStart('/'));
            
            if (File.Exists(fullPath))
            {
                var imageBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
                source.ImageBase64 = Convert.ToBase64String(imageBytes);
                source.MatchHint = textWithoutMetadata;
            }
        }
    }
    
    sources.Add(source);  // 新增：添加到 sources 列表
}
```

#### ② 先发送 sources 元数据

**新增逻辑**：
```csharp
// 先发送 sources 元数据（作为特殊标记的 JSON）
if (sources.Count > 0)
{
    var sourcesJson = System.Text.Json.JsonSerializer.Serialize(new { sources });
    yield return $"[SOURCES]{sourcesJson}[/SOURCES]";
}

// 然后流式生成答案
await foreach (var chunk in _chatClient.GetCompletionStreamAsync(...))
{
    yield return chunk;
}
```

**关键点**：
- 使用 `[SOURCES]...[/SOURCES]` 标记包裹 JSON
- 确保 sources 消息在所有 content 消息之前发送
- JSON 包含完整的图片 Base64 数据

---

### 2. 后端修改 - RAGController.cs

#### 识别并处理 sources 消息

**原来的逻辑**：
```csharp
await foreach (var chunk in _ragService.QueryStreamAsync(request, cancellationToken))
{
    var streamData = new { type = "content", data = chunk };
    var json = JsonSerializer.Serialize(streamData);
    await HttpContext.Response.WriteAsync($"data: {json}\n\n");
}
```

**修改后的逻辑**：
```csharp
await foreach (var chunk in _ragService.QueryStreamAsync(request, cancellationToken))
{
    // 检测是否为 sources 元数据
    if (chunk.StartsWith("[SOURCES]") && chunk.EndsWith("[/SOURCES]"))
    {
        // 提取 sources JSON 并发送
        var sourcesJson = chunk.Substring(9, chunk.Length - 19);
        var streamData = new { type = "sources", data = sourcesJson };
        var json = JsonSerializer.Serialize(streamData);
        await HttpContext.Response.WriteAsync($"data: {json}\n\n");
    }
    else
    {
        // 正常的内容块
        var streamData = new { type = "content", data = chunk };
        var json = JsonSerializer.Serialize(streamData);
        await HttpContext.Response.WriteAsync($"data: {json}\n\n");
    }
    await HttpContext.Response.Body.FlushAsync(cancellationToken);
}
```

---

### 3. 前端修改 - chat.ts (Store)

#### 处理 sources 消息类型

**原来的逻辑**：
```typescript
for await (const chunk of ragApi.queryStream(...)) {
  if (chunk.type === 'content') {
    fullAnswer += chunk.content
    lastMessage.content = fullAnswer
  }
}
```

**修改后的逻辑**：
```typescript
let sources: any[] = []

for await (const chunk of ragApi.queryStream(...)) {
  if (chunk.type === 'sources') {
    // 解析 sources JSON
    try {
      const sourcesData = JSON.parse(chunk.content)
      sources = sourcesData.sources || []
      console.log('Received sources:', sources)
      
      // 更新消息的 sources
      const lastMessage = messages.value[messages.value.length - 1]
      if (lastMessage && lastMessage.role === 'assistant') {
        lastMessage.sources = sources
      }
    } catch (e) {
      console.error('Failed to parse sources:', e)
    }
  } else if (chunk.type === 'content') {
    fullAnswer += chunk.content
    lastMessage.content = fullAnswer
  }
}
```

**关键点**：
- 接收到 `sources` 消息后立即解析 JSON
- 将 `sources` 数组附加到助手消息对象上
- 前端 Vue 组件会自动响应式更新显示

---

## 🎯 数据流示意图

```
用户发起流式查询
    ↓
后端: RAGService.QueryStreamAsync()
    ↓
1. 向量化问题
2. Qdrant 搜索相似文档
3. 构建 sources 列表（加载图片 Base64）
    ↓
4. yield "[SOURCES]{...}[/SOURCES]"  ← 第一条消息
    ↓
前端: ragApi.queryStream() 接收
    ↓
解析 type="sources"，提取 sources 数组
    ↓
更新 message.sources = [...]
    ↓
Vue 组件响应式更新，显示图片
    ↓
5. yield "答案第一部分"  ← content 消息
6. yield "答案第二部分"  ← content 消息
...
    ↓
前端: 逐字累加显示答案
    ↓
完成
```

---

## ✅ 验证测试

### 测试步骤

1. **上传包含文字的图片**
   ```bash
   POST /api/documents/import
   Content-Type: multipart/form-data
   
   file: screenshot.png
   category: 测试
   ```

2. **执行流式 RAG 查询**
   ```bash
   POST /api/rag/query-stream
   Content-Type: application/json
   
   {
     "question": "图片中的内容是什么？",
     "useStream": true,
     "topK": 5
   }
   ```

3. **观察 SSE 响应**
   ```
   data: {"type":"sources","data":"{\"sources\":[{\"fileType\":\"image\",\"imageBase64\":\"iVBORw0...\",\"matchHint\":\"...\"}]}"}
   
   data: {"type":"content","data":"根据"}
   
   data: {"type":"content","data":"图片"}
   
   data: {"type":"content","data":"内容"}
   
   ...
   
   data: {"type":"done","data":""}
   ```

4. **前端验证**
   - 打开浏览器开发者工具 Console
   - 查看日志：`Received sources: [...]`
   - 验证图片显示在来源文档区域
   - 验证答案逐字流式显示

---

## 🔍 调试技巧

### 后端日志
```csharp
_logger.LogInformation("已在流式查询中加载图片 Base64，大小: {Size} KB", imageBytes.Length / 1024);
```

### 前端日志
```typescript
console.log('Received sources:', sources)
console.log('Source 0 has imageBase64:', sources[0]?.imageBase64?.substring(0, 50))
```

### 检查 SSE 流
使用浏览器 Network 面板：
1. 筛选类型：`EventStream`
2. 查看响应内容
3. 验证第一条消息是 `type: "sources"`

---

## 📊 性能考虑

### 图片大小影响
- **小图片 (< 100KB)**：几乎无影响，响应速度快
- **中等图片 (100KB - 1MB)**：首次 sources 消息可能延迟 100-500ms
- **大图片 (> 1MB)**：可能延迟 1-2 秒，建议压缩或使用缩略图

### 优化建议
1. **图片压缩**：导入时自动压缩到合理大小
2. **缩略图方案**：存储缩略图 Base64，点击后加载原图
3. **懒加载**：只在展开来源文档时加载图片
4. **并行处理**：使用 `Task.WhenAll` 并行加载多张图片

---

## 🎉 功能对比

### 修复前
| 功能 | 非流式查询 | 流式查询 |
|------|-----------|---------|
| 返回答案 | ✅ | ✅ |
| 返回 sources | ✅ | ❌ |
| 显示图片 | ✅ | ❌ |
| 实时打字效果 | ❌ | ✅ |

### 修复后
| 功能 | 非流式查询 | 流式查询 |
|------|-----------|---------|
| 返回答案 | ✅ | ✅ |
| 返回 sources | ✅ | ✅ |
| 显示图片 | ✅ | ✅ |
| 实时打字效果 | ❌ | ✅ |

---

## 📌 关键要点

1. ✅ **流式查询现在包含 sources**：第一条消息发送所有来源文档信息
2. ✅ **图片完整加载**：包含 Base64 编码和匹配提示
3. ✅ **前端正确解析**：识别 `type="sources"` 并更新消息对象
4. ✅ **用户体验统一**：流式和非流式查询都能正确显示图片
5. ✅ **性能优化**：图片加载在答案生成前完成，不影响流式体验

---

## 🚀 后续优化方向

1. **图片缓存**：前端缓存已加载的图片 Base64
2. **渐进式加载**：先显示缩略图，点击后加载原图
3. **WebP 格式**：使用 WebP 减少传输大小
4. **CDN 存储**：将图片存储到 OSS/CDN，返回 URL 而非 Base64

---

**修复完成时间**: 2025-01-15  
**影响范围**: 后端 RAGService、RAGController，前端 chat.ts  
**测试状态**: ✅ 编译通过，待端到端测试

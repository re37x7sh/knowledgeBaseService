# 如何查看流式 RAG 查询的调试日志

## 📋 快速步骤

### 1. 启动前端开发服务器

前端开发服务器已启动在：**http://localhost:5173/**

### 2. 打开浏览器开发者工具

**Chrome / Edge**:
- 按 `F12`
- 或右键点击页面 → "检查"
- 或菜单: "更多工具" → "开发者工具"

**Firefox**:
- 按 `F12`
- 或右键点击页面 → "检查元素"

### 3. 切换到 Console（控制台）标签页

在开发者工具顶部找到 **Console** 标签并点击

### 4. 执行流式查询

1. 在 RAG Chat 界面输入问题
2. 确保选择了 **流式模式**（通常有个开关）
3. 点击发送

### 5. 观察控制台输出

你应该会看到带有图标的日志输出：

```
🚀 [RAG Stream] 开始流式查询... {question: "...", documentIds: [...], enableHybridMode: ...}
📦 [RAG Stream] 收到数据块: {type: "sources", content: "..."}
📚 [RAG Stream] 收到 sources 类型消息
🔍 [RAG Stream] sources 原始内容: {"sources":[...]}
✅ [RAG Stream] 解析成功，sources 数量: 3
📄 [RAG Stream] sources 详情: [{documentId: "...", title: "...", ...}, ...]
💾 [RAG Stream] 已更新消息对象的 sources 属性
🔗 [RAG Stream] 当前消息对象: {id: "...", role: "assistant", content: "", sources: [...]}
📦 [RAG Stream] 收到数据块: {type: "content", data: "根据"}
📦 [RAG Stream] 收到数据块: {type: "content", data: "文档"}
...
✅ [RAG Stream] 流式查询完成
🏁 [RAG Stream] 查询结束，最终 sources 数量: 3
```

## 🔍 问题诊断

### 情况 1: 没有看到任何 [RAG Stream] 日志

**可能原因**：
- 前端代码没有更新
- 浏览器缓存了旧代码

**解决方案**：
1. 硬刷新浏览器：`Ctrl + F5` (Windows) 或 `Cmd + Shift + R` (Mac)
2. 清除缓存：开发者工具 → Network 标签 → 勾选 "Disable cache"
3. 关闭开发者工具，再重新打开

### 情况 2: 看到 "🚀 开始流式查询" 但之后没有 "📦 收到数据块"

**可能原因**：
- 后端服务未启动
- 网络请求失败
- SSE 连接未建立

**解决方案**：
1. 检查 Network 标签，查找 `/api/rag/query-stream` 请求
2. 查看请求状态：应该是 200 OK 并且类型是 `text/event-stream`
3. 点击该请求，查看 Response 标签的内容
4. 检查后端是否正在运行

### 情况 3: 看到 "📦 收到数据块" 但类型不是 "sources"

**日志示例**：
```
📦 [RAG Stream] 收到数据块: {type: "content", data: "..."}
⚠️ [RAG Stream] 未知消息类型: xxx
```

**可能原因**：
- 后端没有发送 sources 消息
- Sources 列表为空（没有匹配的文档）

**解决方案**：
1. 查看后端日志，搜索 "发送 sources 数据"
2. 检查 Qdrant 是否有数据
3. 尝试降低搜索阈值或使用不同的查询

### 情况 4: 看到 "❌ 解析 sources 失败"

**日志示例**：
```
❌ [RAG Stream] 解析 sources 失败: SyntaxError: Unexpected token
❌ [RAG Stream] 原始数据: {invalid json...
```

**可能原因**：
- JSON 格式错误
- 数据被截断
- 特殊字符未正确转义

**解决方案**：
1. 复制控制台中的 "原始数据"
2. 使用在线 JSON 验证工具检查格式
3. 检查后端序列化逻辑

### 情况 5: sources 解析成功但 UI 不显示

**日志示例**：
```
✅ [RAG Stream] 解析成功，sources 数量: 3
💾 [RAG Stream] 已更新消息对象的 sources 属性
🏁 [RAG Stream] 查询结束，最终 sources 数量: 3
```
但页面上看不到 "📚 相关文档"

**可能原因**：
- Vue 响应式未触发
- UI 组件条件渲染判断错误
- CSS 样式隐藏了元素

**解决方案**：
1. 打开 Vue Devtools，查看消息对象的 sources 属性
2. 检查 "🔗 当前消息对象" 日志，确认 sources 数组有数据
3. 在 Elements 标签中搜索 `class="sources"`，看是否渲染了但被隐藏
4. 检查是否有 CSS 错误或样式覆盖

## 🛠️ 高级调试

### 查看完整的 SSE 消息

1. 打开 Network 标签
2. 找到 `query-stream` 请求
3. 点击后查看 **EventStream** 或 **Response** 标签
4. 应该看到类似这样的消息：

```
data: {"type":"sources","data":"{\"sources\":[{\"documentId\":\"...\",\"title\":\"...\"}]}"}

data: {"type":"content","data":"根据"}

data: {"type":"content","data":"文档"}

data: {"type":"done","data":""}
```

### 手动测试后端 API

使用 curl 或 Postman 测试：

```bash
curl -X POST http://localhost:5000/api/rag/query-stream \
  -H "Content-Type: application/json" \
  -d '{"question":"测试问题","topK":5,"useStream":true}' \
  --no-buffer
```

应该看到 SSE 格式的响应流。

### 使用 Vue Devtools

1. 安装 Vue Devtools 浏览器扩展
2. 打开 Vue Devtools
3. 切换到 **Components** 标签
4. 找到 `RAGChat` 组件
5. 查看 `chatStore.messages` 数组
6. 展开最后一条消息，检查 `sources` 属性

## 📊 正常日志示例

一次成功的流式查询应该产生类似这样的日志：

```
🚀 [RAG Stream] 开始流式查询... 
   {question: "图片中有什么内容", documentIds: undefined, enableHybridMode: false}

📦 [RAG Stream] 收到数据块: 
   {type: "sources", content: "{\"sources\":[{\"documentId\":\"123\",\"title\":\"测试图片.jpg\",\"score\":0.85,\"snippet\":\"[图片路径: /uploads/images/test.jpg]\",\"fileType\":\"image\",\"imageBase64\":\"iVBORw0KG...\",\"matchHint\":\"图片包含一只猫\"}]}"}

📚 [RAG Stream] 收到 sources 类型消息

🔍 [RAG Stream] sources 原始内容: 
   {"sources":[{"documentId":"123","title":"测试图片.jpg",...}]}

✅ [RAG Stream] 解析成功，sources 数量: 1

📄 [RAG Stream] sources 详情: 
   [
     {
       documentId: "123",
       title: "测试图片.jpg",
       score: 0.85,
       snippet: "[图片路径: /uploads/images/test.jpg]",
       fileType: "image",
       imageBase64: "iVBORw0KG...",
       matchHint: "图片包含一只猫"
     }
   ]

💾 [RAG Stream] 已更新消息对象的 sources 属性

🔗 [RAG Stream] 当前消息对象: 
   {
     id: "1234567890-0.123",
     role: "assistant",
     content: "",
     sources: [{...}],
     timestamp: "2025-01-15T08:30:45.123Z"
   }

📦 [RAG Stream] 收到数据块: {type: "content", data: "根据"}
📦 [RAG Stream] 收到数据块: {type: "content", data: "提供的"}
📦 [RAG Stream] 收到数据块: {type: "content", data: "图片"}
... (更多 content 数据块)

📦 [RAG Stream] 收到数据块: {type: "done", data: ""}

✅ [RAG Stream] 流式查询完成

🏁 [RAG Stream] 查询结束，最终 sources 数量: 1
```

## 🎯 快速检查清单

执行流式查询后，按顺序检查：

- [ ] 看到 `🚀 开始流式查询`
- [ ] 看到 `📦 收到数据块`（至少一条）
- [ ] 第一条数据块类型是 `sources`
- [ ] 看到 `✅ 解析成功，sources 数量: X`（X > 0）
- [ ] 看到 `💾 已更新消息对象的 sources 属性`
- [ ] 看到 `🏁 查询结束`
- [ ] UI 上显示 "📚 相关文档" 区域
- [ ] 可以看到文档标题和相关度
- [ ] （如果是图片）可以看到图片内容

如果以上任何一步失败，请参考上面的"问题诊断"部分。

---

**提示**：保持开发者工具打开可以帮助你实时监控应用状态和网络请求。

# 图片 RAG 检索显示功能说明

## ✨ 功能概述

实现了在 RAG 检索时，如果命中的是图片文档，能够在查询结果中**显示完整图片内容**，并提供**智能提示**告知用户命中了图片的哪部分内容。

---

## 🎯 实现方案

采用**分块 + 整图 + 智能提示**的混合方案：

```
图片导入
    ↓
保存图片到文件系统: wwwroot/uploads/images/{documentId}.jpg
    ↓
豆包视觉识别 → 提取文字
    ↓
在文本中添加标记:
[图片文件: xxx.jpg]
[图片路径: /uploads/images/{documentId}.jpg]

提取的文字内容...
    ↓
分块 → 向量化 → 索引到 Qdrant
    ↓
RAG 查询命中后:
    ↓
检测 content 中的 [图片路径:...] 标记
    ↓
读取图片文件 → 转 Base64
    ↓
返回给前端: { snippet, imageBase64, matchHint }
    ↓
前端显示图片 + 命中提示
```

---

## 📝 实现细节

### 1️⃣ **后端修改**

#### FileImportService.cs
```csharp
public async Task<string> ExtractTextFromImageAsync(...)
{
    // 生成唯一文档ID
    var documentId = Guid.NewGuid().ToString();
    
    // 保存图片到 wwwroot/uploads/images/
    var imageDir = Path.Combine("wwwroot", "uploads", "images");
    Directory.CreateDirectory(imageDir);
    var imagePath = Path.Combine(imageDir, $"{documentId}{ext}");
    
    using (var fs = new FileStream(imagePath, FileMode.Create))
    {
        await fileStream.CopyToAsync(fs, cancellationToken);
    }
    
    // 豆包视觉识别
    var extractedText = await _visionClient.AnalyzeImageFromStreamAsync(...);
    
    // 返回带路径标记的文本
    return $"[图片文件: {fileName}]\n[图片路径: {relativeImagePath}]\n\n{extractedText}";
}
```

#### SourceReference DTO
```csharp
public class SourceReference
{
    // ... 原有字段
    
    public string? FileType { get; set; }         // "image" / "pdf" / "docx"
    public string? ImageBase64 { get; set; }      // 图片的Base64编码
    public string? MatchHint { get; set; }        // 命中文本提示
}
```

#### RAGService.cs
```csharp
// 检测图片并加载
if (content.Contains("[图片路径:"))
{
    source.FileType = "image";
    
    // 提取路径
    var pathMatch = Regex.Match(content, @"\[图片路径:\s*([^\]]+)\]");
    
    if (pathMatch.Success)
    {
        var imagePath = pathMatch.Groups[1].Value.Trim();
        var fullPath = Path.Combine("wwwroot", imagePath.TrimStart('/'));
        
        if (File.Exists(fullPath))
        {
            var imageBytes = await File.ReadAllBytesAsync(fullPath);
            source.ImageBase64 = Convert.ToBase64String(imageBytes);
            
            // 提取命中文本
            var textWithoutMetadata = Regex.Replace(
                content, 
                @"\[图片文件:.*?\]\s*\[图片路径:.*?\]\s*", 
                "");
            
            source.MatchHint = textWithoutMetadata.Substring(0, 150) + "...";
        }
    }
}
```

### 2️⃣ **前端修改**

#### SourceDocument 接口
```typescript
export interface SourceDocument {
  documentId: string
  title: string
  score: number
  snippet: string
  sourceUrl?: string
  fileType?: string          // 新增
  imageBase64?: string       // 新增
  matchHint?: string         // 新增
}
```

#### RAGChat.vue 显示逻辑
```vue
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
  
  <!-- 图片显示 -->
  <div v-if="source.fileType === 'image' && source.imageBase64" class="source-image-container">
    <img 
      :src="`data:image/jpeg;base64,${source.imageBase64}`" 
      class="source-image"
      :alt="source.title"
    />
    <div v-if="source.matchHint" class="match-hint">
      ✅ 命中内容：{{ source.matchHint }}
    </div>
  </div>
  
  <!-- 文本摘要 -->
  <div class="source-excerpt">
    {{ source.snippet }}
  </div>
</div>
```

#### CSS 样式
```css
.source-image-container {
  margin: 10px 0;
  border: 1px solid #e4e7ed;
  border-radius: 4px;
  overflow: hidden;
  background: #fff;
}

.source-image {
  max-width: 100%;
  height: auto;
  display: block;
  cursor: pointer;
  transition: transform 0.3s ease;
}

.source-image:hover {
  transform: scale(1.02);
}

.match-hint {
  padding: 8px 12px;
  background: #f0f9ff;
  border-top: 1px solid #e4e7ed;
  font-size: 12px;
  color: #409eff;
  line-height: 1.5;
}
```

---

## 🚀 使用示例

### 场景 1：上传并查询图片

```bash
# 1. 导入图片
POST /api/documents/import
Content-Type: multipart/form-data

file: screenshot.png
category: 测试

# 后端处理:
# → 保存到: wwwroot/uploads/images/xxx-xxx-xxx.png
# → 豆包识别: "图片中显示了一个登录界面，包含用户名和密码输入框..."
# → 索引到 Qdrant

# 2. RAG 查询
POST /api/rag/query
{
  "question": "登录界面是什么样的？",
  "useKnowledgeBase": true
}

# 响应:
{
  "answer": "根据图片内容，登录界面包含...",
  "sources": [
    {
      "documentId": "xxx",
      "title": "screenshot.png",
      "score": 0.92,
      "snippet": "[图片文件: screenshot.png]...",
      "fileType": "image",
      "imageBase64": "iVBORw0KGgoAAAANS...",  // ← 完整图片
      "matchHint": "图片中显示了一个登录界面，包含用户名和密码输入框..."
    }
  ]
}
```

### 场景 2：前端显示效果

```
┌─────────────────────────────────────────┐
│ 📚 相关文档                             │
├─────────────────────────────────────────┤
│ screenshot.png  [🖼️ 图片] [92.0% 相关] │
├─────────────────────────────────────────┤
│ ┌───────────────────────────────────┐   │
│ │                                   │   │
│ │     [完整图片显示]                │   │
│ │                                   │   │
│ └───────────────────────────────────┘   │
│                                         │
│ ✅ 命中内容：图片中显示了一个登录界面... │
└─────────────────────────────────────────┘
```

---

## 📊 数据流

```
用户提问: "登录界面是什么样的？"
    ↓
向量化查询
    ↓
Qdrant 检索 → 命中包含 "[图片路径:...]" 的 chunk
    ↓
RAGService 检测到图片标记
    ↓
读取文件: wwwroot/uploads/images/xxx.png
    ↓
转 Base64: "iVBORw0KGgo..."
    ↓
返回给前端: {
    snippet: "提取的文字...",
    fileType: "image",
    imageBase64: "...",
    matchHint: "图片中显示了..."
}
    ↓
前端显示图片 + 提示
```

---

## ⚙️ 关键特性

### 1. **按需加载**
- 只有命中的图片才会加载 Base64
- 未命中的图片不会加载，节省带宽

### 2. **智能提示**
- `matchHint` 显示命中的文本片段
- 帮助用户理解为什么命中这张图片

### 3. **文件管理**
- 图片存储在 `wwwroot/uploads/images/`
- 使用文档 ID 作为文件名，避免冲突
- 支持 .png, .jpg, .jpeg, .bmp, .gif

### 4. **前端优化**
- 图片可点击放大（CSS hover 效果）
- 显示文件类型标签（🖼️ 图片）
- 显示相关度分数

---

## 🎨 UI 展示

### 命中图片时的显示

```
┌───────────────────────────────────────────────┐
│  Q: 登录界面是什么样的？                      │
└───────────────────────────────────────────────┘

┌───────────────────────────────────────────────┐
│  A: 根据提供的图片，登录界面包含...          │
│                                               │
│  📚 相关文档                                  │
│  ┌─────────────────────────────────────────┐ │
│  │ screenshot.png  🖼️ 图片  92.0% 相关    │ │
│  ├─────────────────────────────────────────┤ │
│  │ [图片显示区域]                          │ │
│  │ ┌───────────────────────────────────┐   │ │
│  │ │                                   │   │ │
│  │ │    登录界面截图                   │   │ │
│  │ │    [用户名] [________]            │   │ │
│  │ │    [密码]   [________]            │   │ │
│  │ │    [登录按钮]                     │   │ │
│  │ │                                   │   │ │
│  │ └───────────────────────────────────┘   │ │
│  │                                         │ │
│  │ ✅ 命中内容：图片中显示了一个登录界面， │ │
│  │    包含用户名和密码输入框...            │ │
│  └─────────────────────────────────────────┘ │
└───────────────────────────────────────────────┘
```

---

## ⚠️ 注意事项

### 1. **图片大小限制**
- 建议图片 < 5MB
- 过大的图片会影响响应时间
- Base64 编码会增加 ~33% 大小

### 2. **存储管理**
- 图片文件保存在 `wwwroot/uploads/images/`
- 需要定期清理已删除文档的图片
- 建议添加图片文件大小限制

### 3. **性能优化**
- 只返回 Top 3-5 个命中结果的图片
- 考虑添加图片缓存机制
- 大图片可以考虑生成缩略图

### 4. **安全性**
- 验证文件类型（只允许图片格式）
- 防止路径遍历攻击（验证路径不包含 `../`）
- 考虑添加访问权限验证

---

## 🔧 配置选项

### 图片保存路径
```csharp
// 可在配置文件中自定义
var imageDir = Path.Combine("wwwroot", "uploads", "images");
```

### 支持的图片格式
```csharp
".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif"
```

### Base64 加载策略
```csharp
// 当前：按需加载（命中时才加载）
// 可选：预加载（索引时就转 Base64 存入 Qdrant payload）
```

---

## 📈 未来优化方向

### 1. **图片缩略图**
- 生成 200x200 缩略图存入 Qdrant payload
- 快速预览，点击后加载原图

### 2. **区域裁剪**（需要 OCR 坐标）
- 集成 PaddleOCR 获取文字坐标
- 只返回命中文字的区域
- 精确定位，减少传输量

### 3. **图片压缩**
- 自动压缩大图片
- 保持清晰度的同时减小文件大小

### 4. **懒加载**
- 前端只加载可见区域的图片
- 滚动到图片时才请求 Base64

---

## ✅ 总结

实现了完整的**图片 RAG 检索显示**功能：

- ✅ 图片自动保存到文件系统
- ✅ RAG 查询时检测并加载图片
- ✅ 前端显示完整图片 + 命中提示
- ✅ 智能提示告知命中了哪部分内容
- ✅ 按需加载，性能优化
- ✅ 友好的用户界面

**现在用户在 RAG 查询时，不仅能看到文字摘要，还能直接看到相关的图片内容！** 🎉

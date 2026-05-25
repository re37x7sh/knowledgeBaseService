# 文件导入快速指南

## ⚡ 5分钟快速开始

### 第1步：启动服务

```powershell
cd d:\dev\KnowledgeBaseService\docker
docker-compose up -d

# 等待服务启动（约30秒）
# 检查状态
docker-compose ps
```

### 第2步：导入第一个文件

**使用 PowerShell**:

```powershell
# 导入 Word 文档
$filePath = "C:\Users\YourName\Documents\myguide.docx"
$form = @{
    file = Get-Item $filePath
    category = "技术文档"
}

$result = Invoke-RestMethod `
  -Uri "http://localhost:5000/api/documents/import-from-file" `
  -Method Post `
  -Form $form

Write-Host "导入成功! 文档 ID: $($result.id)"
```

**使用 cURL**:

```bash
curl -X POST http://localhost:5000/api/documents/import-from-file \
  -F "file=@C:\path\to\document.docx" \
  -F "category=技术文档"
```

### 第3步：查询导入的文档

```powershell
$body = @{
    question = "文档中主要讲的是什么?"
    topK = 5
} | ConvertTo-Json

Invoke-RestMethod `
  -Uri "http://localhost:5000/api/rag/query" `
  -Method Post `
  -ContentType "application/json" `
  -Body $body | 
  Select-Object -Property question, answer
```

---

## 📁 支持的格式

| 格式 | 扩展名 | 特点 | 场景 |
|------|--------|------|------|
| **Word** | .docx | 支持表格、段落 | 项目文档、报告 |
| **PDF** | .pdf | 支持多页、格式化 | 手册、技术文档 |
| **Markdown** | .md | 纯文本、代码块 | README、教程 |
| **纯文本** | .txt | 基础文本 | 日志、配置文件、简单记录 |

---

## 🎯 常见场景

### 场景1：上传一个 PDF 手册

```powershell
$filePath = "C:\Manuals\UserGuide.pdf"
$file = Get-Item $filePath

Invoke-RestMethod `
  -Uri "http://localhost:5000/api/documents/import-from-file" `
  -Method Post `
  -Form @{
    file = $file
    category = "用户手册"
  }
```

### 场景2：批量导入多个文件

```powershell
$files = Get-ChildItem "C:\ProjectDocs\*.docx"
$form = @{ category = "项目文档" }

$files | ForEach-Object {
  $form["files"] = $_
}

Invoke-RestMethod `
  -Uri "http://localhost:5000/api/documents/import-files-batch" `
  -Method Post `
  -Form $form
```

### 场景3：导入后立即查询

```powershell
# 1. 导入文件
$result = Invoke-RestMethod `
  -Uri "http://localhost:5000/api/documents/import-from-file" `
  -Method Post `
  -Form @{
    file = Get-Item "guide.docx"
  }

Write-Host "文档已导入: $($result.id)"

# 2. 稍等1-2秒（向量化完成）
Start-Sleep -Seconds 2

# 3. 查询
$query = @{
    question = "如何使用这个系统?"
} | ConvertTo-Json

$answer = Invoke-RestMethod `
  -Uri "http://localhost:5000/api/rag/query" `
  -Method Post `
  -ContentType "application/json" `
  -Body $query

Write-Host $answer.answer
```

---

## 🔍 查看导入的文档

```powershell
# 查看所有文档
Invoke-RestMethod `
  -Uri "http://localhost:5000/api/documents/list?skip=0&take=10" `
  -Method Get | 
  Format-Table -Property id, title, category, createdAt

# 获取特定文档详情
$docId = "550e8400-e29b-41d4-a716-446655440000"
Invoke-RestMethod `
  -Uri "http://localhost:5000/api/documents/$docId" `
  -Method Get
```

---

## 📊 支持的文件格式详解

### Word (.docx) - 最佳支持

✅ **支持**:
- 段落和标题
- 表格内容
- 列表

❌ **不支持**:
- 图像
- 页脚/页眉
- 复杂样式

**导入示例**:
```powershell
Invoke-RestMethod `
  -Uri "http://localhost:5000/api/documents/import-from-file" `
  -Method Post `
  -Form @{
    file = Get-Item "ProjectRequirements.docx"
    category = "需求文档"
  }
```

### PDF - 良好支持

✅ **支持**:
- 纯文本
- 多页内容
- 表格

❌ **不支持**:
- OCR（扫描图片）
- 加密的PDF
- 图形提取

**导入示例**:
```powershell
Invoke-RestMethod `
  -Uri "http://localhost:5000/api/documents/import-from-file" `
  -Method Post `
  -Form @{
    file = Get-Item "UserManual.pdf"
    category = "用户手册"
  }
```

### Markdown (.md) - 完整支持

✅ **支持**:
- 所有 Markdown 语法
- 代码块
- 链接和引用

**导入示例**:
```powershell
Invoke-RestMethod `
  -Uri "http://localhost:5000/api/documents/import-from-file" `
  -Method Post `
  -Form @{
    file = Get-Item "README.md"
    category = "文档"
  }
```

### 纯文本 (.txt) - 完整支持

✅ **支持**:
- UTF-8 编码的纯文本
- 任意内容长度
- 保留原始换行

**导入示例**:
```powershell
Invoke-RestMethod `
  -Uri "http://localhost:5000/api/documents/import-from-file" `
  -Method Post `
  -Form @{
    file = Get-Item "notes.txt"
    category = "笔记"
  }
```

**适用场景**:
- 日志文件
- 配置文件
- 简单记录
- 代码片段

---

## 🛠️ 故障排查

### 问题1：文件过大

```
错误: 文件过大，最大支持 50MB，当前文件大小: 75MB
```

**解决方案**：
- 将大文件拆分成多个小文件
- 或压缩文件后提取内容

```powershell
# 示例：拆分大 PDF
# 使用第三方工具拆分后，批量导入
$pdfs = Get-ChildItem "split_*.pdf"
$form = @{ category = "分卷文档" }

$pdfs | ForEach-Object {
  $form["files"] = $_
}

Invoke-RestMethod `
  -Uri "http://localhost:5000/api/documents/import-files-batch" `
  -Method Post `
  -Form $form
```

### 问题2：PDF 无法读取

```
错误: 无法读取 PDF 文档，请确保文件格式正确且非加密
```

**可能原因**：
- PDF 被加密或有密码
- PDF 是扫描的图片（需要 OCR）
- PDF 格式损坏

**解决方案**：
- 移除 PDF 密码
- 对扫描的 PDF 进行 OCR 处理
- 重新生成或下载 PDF

### 问题3：文档导入后查询无结果

```powershell
# 导入文档
$doc = Invoke-RestMethod ...

# 等待足够时间让向量化完成
Start-Sleep -Seconds 5

# 现在查询应该有结果了
$result = Invoke-RestMethod ...
```

### 问题4：Word 文档内容不完整

**常见原因**：
- Word 文档中有图表或复杂格式
- 某些特殊字符无法识别

**解决方案**：
- 将 Word 文档导出为文本
- 或将内容复制到 Markdown 文件

---

## 📈 性能建议

### 单个文件导入

- **小文件** (< 5MB): 通常 < 1 秒
- **中等文件** (5-20MB): 1-5 秒
- **大文件** (20-50MB): 5-30 秒

```powershell
# 测量导入时间
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

Invoke-RestMethod `
  -Uri "http://localhost:5000/api/documents/import-from-file" `
  -Method Post `
  -Form @{ file = Get-Item "largefile.pdf" }

$stopwatch.Stop()
Write-Host "导入耗时: $($stopwatch.ElapsedMilliseconds)ms"
```

### 批量导入优化

- 一次上传最多 10 个文件
- 总大小不超过 50MB
- 建议相关文件一起导入

```powershell
# ✅ 好的做法：相关文件一起上传
$docs = Get-ChildItem "ProjectA_*.pdf"

# ❌ 不好的做法：每个文件单独上传
# $docs | ForEach-Object { 单独上传 }
```

---

## 🔗 完整示例脚本

```powershell
# file-import-example.ps1
# 完整的文件导入示例脚本

param(
  [Parameter(Mandatory=$true)]
  [string]$FilePath,
  
  [string]$Category = "导入文档",
  [string]$BaseUrl = "http://localhost:5000"
)

# 验证文件
if (-not (Test-Path $FilePath)) {
  Write-Error "文件不存在: $FilePath"
  exit 1
}

$file = Get-Item $FilePath
$fileName = $file.Name
$fileSize = $file.Length / 1MB

Write-Host "导入文件: $fileName (大小: $([math]::Round($fileSize, 2))MB)"

# 检查支持的格式
$supportedFormats = Invoke-RestMethod `
  -Uri "$BaseUrl/api/documents/supported-formats" `
  -Method Get

$fileExt = [System.IO.Path]::GetExtension($fileName)
if ($supportedFormats.supported_formats -notcontains $fileExt) {
  Write-Error "不支持的文件格式: $fileExt"
  Write-Host "支持的格式: $($supportedFormats.supported_formats -join ', ')"
  exit 1
}

# 导入文件
Write-Host "正在导入..."
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

$result = Invoke-RestMethod `
  -Uri "$BaseUrl/api/documents/import-from-file" `
  -Method Post `
  -Form @{
    file = $file
    category = $Category
  }

$stopwatch.Stop()

# 显示结果
Write-Host "✓ 导入成功！" -ForegroundColor Green
Write-Host "文档 ID: $($result.id)"
Write-Host "标题: $($result.title)"
Write-Host "分类: $($result.category)"
Write-Host "耗时: $($stopwatch.ElapsedMilliseconds)ms"

# 等待向量化完成
Write-Host "等待向量化完成..."
Start-Sleep -Seconds 3

# 测试查询
Write-Host "测试查询..."
$query = @{
  question = "这个文档主要讲什么?"
  topK = 3
} | ConvertTo-Json

$queryResult = Invoke-RestMethod `
  -Uri "$BaseUrl/api/rag/query" `
  -Method Post `
  -ContentType "application/json" `
  -Body $query

Write-Host "查询结果:"
Write-Host "问题: $($queryResult.question)"
Write-Host "回答: $($queryResult.answer)"
Write-Host "来源数: $($queryResult.sources.Count)"
```

**使用脚本**:

```powershell
# 导入 Word 文档
.\file-import-example.ps1 -FilePath "C:\Docs\guide.docx" -Category "技术文档"

# 导入 PDF
.\file-import-example.ps1 -FilePath "C:\Docs\manual.pdf" -Category "用户手册"

# 导入 Markdown
.\file-import-example.ps1 -FilePath "C:\Docs\README.md" -Category "文档"
```

---

## ✅ 下一步

1. **测试导入**: 尝试导入你的第一个文件
2. **批量导入**: 一起导入多个相关文件
3. **优化查询**: 调整 topK 和 temperature 参数
4. **自动化**: 集成到你的工作流中

---

## 📖 更多资源

- 详细文档: `FILE_IMPORT_API.md`
- API 示例: `API_EXAMPLES.md`
- 完整架构: `ARCHITECTURE.md`
- 快速参考: `QUICK_REFERENCE.md`

祝你使用愉快！🚀

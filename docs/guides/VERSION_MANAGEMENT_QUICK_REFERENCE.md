# 📚 版本管理快速参考

## 🎯 核心 API（10 个端点）

### 版本列表
```
GET /api/documentversions/document/{docId}?skip=0&take=20
→ 返回: List<VersionResponse>
```

### 版本内容
```
GET /api/documentversions/{versionId}/content
→ 返回: VersionContentResponse
```

### 创建版本
```
POST /api/documentversions/create
{
  "documentId": "doc-123",
  "content": "内容",
  "title": "标题",
  "changeLog": "变更说明",
  "createdBy": "用户",
  "tag": "v1.0"
}
→ 返回: 201 + VersionResponse
```

### 版本比较
```
GET /api/documentversions/document/{docId}/compare?fromVersion=1&toVersion=2
→ 返回: CompareVersionResponse
  linesAdded, linesRemoved, linesModified
```

### 回滚版本
```
POST /api/documentversions/document/{docId}/rollback?targetVersion=1&reason=xxx
→ 返回: 200 OK
```

### 添加标签
```
POST /api/documentversions/{versionId}/tag?tag=v1.0
→ 返回: 200 OK
```

### 删除版本
```
DELETE /api/documentversions/{versionId}
→ 返回: 200 OK (不能删除当前版本)
```

### 统计信息
```
GET /api/documentversions/document/{docId}/statistics
→ 返回: VersionStatisticsResponse
  totalVersions, averageSize, maxSize, tags 等
```

### 导出版本
```
GET /api/documentversions/{versionId}/export?format=markdown|text|html
→ 返回: 文件下载
```

### 当前版本
```
GET /api/documentversions/document/{docId}/current
→ 返回: VersionContentResponse
```

---

## 🎨 前端 UI

### 集成位置
- 路径: `KnowledgeBaseService.Web/src/components/VersionManager.vue`
- 已集成到: `MainLayout.vue` 的"版本管理"菜单项

### 使用方式
```vue
<VersionManager 
  :document-id="selectedDocumentId"
  :document-title="selectedDocumentTitle"
/>
```

### 4 个功能模块
1. **版本列表** - 查看、导出、删除版本
2. **版本比较** - Diff 分析
3. **统计信息** - 编辑统计、大小统计
4. **回滚管理** - 快速版本回滚

---

## 💡 常用场景

### 场景 1: 修改文档并保存版本
```powershell
# 1. 编辑文档（通过 UI 或 API）

# 2. 创建新版本
$body = @{
    documentId = "doc-123"
    content = "新内容..."
    title = "标题"
    changeLog = "修复了问题 #123"
    createdBy = "admin"
    tag = "hotfix-1.0.1"
} | ConvertTo-Json

curl -X POST http://localhost:5000/api/documentversions/create `
  -H "Content-Type: application/json" `
  -d $body
```

### 场景 2: 查看变更历史
```powershell
# 获取所有版本
curl http://localhost:5000/api/documentversions/document/doc-123

# 比较两个版本
curl "http://localhost:5000/api/documentversions/document/doc-123/compare?fromVersion=1&toVersion=2"
```

### 场景 3: 快速回滚
```powershell
# 发现最新版本有问题，立即回滚到上一个版本
curl -X POST "http://localhost:5000/api/documentversions/document/doc-123/rollback?targetVersion=3&reason=修复%20bug"
```

### 场景 4: 标记重要版本
```powershell
# 标记一个版本为发布版本
curl -X POST "http://localhost:5000/api/documentversions/version-123/tag?tag=release-v1.0"
```

---

## 🔧 技术栈

| 层 | 技术 |
|---|---|
| **后端服务** | C# .NET 8, ASP.NET Core |
| **存储** | 内存字典（可扩展为数据库）|
| **前端** | Vue3, TypeScript, Tailwind CSS |
| **API** | RESTful, JSON |

---

## ⚡ 性能

| 操作 | 耗时 |
|---|---|
| 创建版本 | <100ms |
| 获取版本列表 | <50ms |
| 版本比较 | <200ms |
| 回滚版本 | <100ms |
| 导出版本 | <500ms |
| 获取统计 | <100ms |

---

## 📊 版本实体属性

```csharp
{
  id: "uuid",              // 版本 ID
  documentId: "doc-123",   // 关联文档
  versionNumber: 1,        // 版本号
  title: "文档标题",       // 标题
  content: "...",          // 内容快照
  tag: "v1.0",            // 版本标签
  changeLog: "更新说明",   // 变更说明
  changeSummary: "新增5行", // 自动摘要
  category: "技术文档",    // 分类
  createdBy: "admin",      // 编辑者
  createdAt: "2024-01-15", // 创建时间
  isCurrent: true,         // 是否当前
  contentSize: 2048,       // 大小（字节）
}
```

---

## 🚀 快速启动

```bash
# 1. 后端
cd d:\dev\KnowledgeBaseService
dotnet run

# 2. 前端
cd KnowledgeBaseService.Web
npm run dev

# 3. 访问
open http://localhost:5173
click 版本管理 菜单
```

---

## 📝 注意事项

✅ **支持**:
- 无限制版本历史
- 并发读取
- 事务性回滚

❌ **限制**:
- 应用重启后数据丢失（使用内存存储）
- 不支持并发编辑冲突解决
- 无权限隔离

🔧 **可以改进**:
- 接入 SQL Server/PostgreSQL
- 添加权限控制
- 实现自动备份

---

## 📚 相关文档

- [完整实现指南](./DOCUMENT_VERSION_MANAGEMENT.md)
- [API 详细文档](./FILE_IMPORT_API.md)
- [快速开始](./QUICKSTART.md)


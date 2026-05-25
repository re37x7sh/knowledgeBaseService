# AGENTS.md — KnowledgeBaseService 快速参考手册

> AI 工具和新成员快速上手指南。读完此文件即可参与开发，无需通读所有代码。

---

## 项目是什么

基于 **Qdrant + 豆包 Ark API + C# .NET 8** 的 RAG 知识库服务。

核心能力：
- 导入 15 种文件（Word / PDF / Excel / PPT / 图片 / Markdown / CSV / JSONL）→ 向量化存储
- 语义搜索 + LLM 问答（RAG）
- 文档版本管理（历史 / 对比 / 回滚）
- 对话长期记忆（向量 + 结构化混合存储）

运行后访问：`http://localhost:5000/swagger`

---

## 快速启动

```bash
# 1. 启动依赖（Qdrant + PostgreSQL + Redis）
docker-compose -f docker/docker-compose.yml up -d

# 2. 构建
dotnet build

# 3. 运行
dotnet run --project KnowledgeBaseService.Api

# 4. 测试
dotnet test
```

环境变量（替换 appsettings.json 中的占位符）：
```
DEEPSEEK_API_KEY=sk-...
LLM_API_KEY=...
```

---

## 架构（4 层 Clean Architecture）

```
KnowledgeBaseService.Api           ← 入口：路由、中间件、DI 注册
KnowledgeBaseService.Application   ← 业务：Service、接口定义、DTO
KnowledgeBaseService.Infrastructure← 外部：Qdrant、Ark API、PostgreSQL
KnowledgeBaseService.Core          ← 领域：实体、常量（零依赖）
```

**依赖方向**：Api → Application → Infrastructure → Core（单向，不可逆）

跨层数据传输只用 `Application/DTOs/`，实体类（Core/Entities）不出 Infrastructure 层。

---

## 关键文件地图

### API 控制器

| 文件 | 职责 |
|------|------|
| `Api/Controllers/RAGController.cs` | RAG 查询（流式 + 非流式） |
| `Api/Controllers/DocumentsController.cs` | 文档 CRUD、文件导入 |
| `Api/Controllers/DocumentVersionsController.cs` | 版本管理（10 个端点） |
| `Api/Controllers/MemoryController.cs` | 对话长期记忆 CRUD |
| `Api/Controllers/QdrantManagementController.cs` | 向量库运维接口 |

### 业务服务

| 文件 | 职责 |
|------|------|
| `Application/Services/RAGService.cs` | RAG 核心：检索 + 生成 |
| `Application/Services/DocumentService.cs` | 文档管理业务逻辑 |
| `Application/Services/FileImportService.cs` | 多格式文件解析与导入 |
| `Application/Services/DocumentVersionService.cs` | 版本创建/对比/回滚 |
| `Application/Services/ConversationMemoryService.cs` | 长期记忆读写 |
| `Application/Services/HybridSearchService.cs` | 向量 + BM25 混合检索 |
| `Application/Services/SemanticTextSplitter.Optimized.cs` | 语义分块（当前主用） |

### 基础设施客户端

| 文件 | 职责 |
|------|------|
| `Infrastructure/Clients/DoubaoEmbeddingClient.cs` | 文本向量化（2560 维） |
| `Infrastructure/Clients/DoubaoChatClient.cs` | LLM 对话生成 |
| `Infrastructure/Clients/DoubaoVisionClient.cs` | 图片 / PPT 视觉识别 |
| `Infrastructure/Clients/QdrantHttpClient.cs` | 向量数据库操作 |
| `Infrastructure/Repositories/DocumentRepository.cs` | PostgreSQL 文档持久化 |
| `Infrastructure/Repositories/DocumentVersionRepository.cs` | 版本数据持久化 |

### 核心实体

| 实体 | 说明 |
|------|------|
| `Core/Entities/Document.cs` | 文档（含 FileExtension 字段） |
| `Core/Entities/DocumentVersion.cs` | 版本快照（13 个属性） |
| `Core/Entities/ConversationMemory.cs` | 记忆条目（重要性评分 + 时间衰减） |
| `Core/Entities/SearchResult.cs` | 检索结果 |

### 接口定义（Application/Interfaces/）

所有 Service 对应 `I{ServiceName}.cs` 接口，新增功能先在此定义接口。

---

## 配置速查（appsettings.json）

| 键 | 说明 |
|----|------|
| `LLM.EmbeddingModel` | 当前：`doubao-embedding-text-240715` |
| `LLM.ChatModel` | 当前：`doubao-1-5-pro-256k-250115` |
| `LLM.VectorDimension` | `2560`（与 Qdrant collection 必须一致） |
| `RAG.UseSemanticChunking` | `true` = 使用语义分块 |
| `RAG.UseHybridSearch` | `true` = 向量(0.7) + BM25(0.3) 混合 |
| `RAG.UseSemanticKernel` | 是否启用 Semantic Kernel 路径 |
| `ConnectionStrings.DefaultConnection` | PostgreSQL 连接字符串 |
| `Qdrant.BaseUrl` | 向量数据库地址 |
| `Redis.Enabled` | 是否启用分块缓存 |

---

## 编码约定（必须遵守）

```
✅ 路由全小写          LowercaseUrls = true
✅ JSON 输出 camelCase  输入大小写不敏感
✅ 异步方法加后缀      XxxAsync()
✅ Controller 无业务逻辑  通过 IXxxService 调用
✅ 新功能先定义接口    Application/Interfaces/IXxxService.cs
✅ 跨层只用 DTO        Core/Entities 不出 Infrastructure
```

---

## ⚠️ CHANGELOG 更新规则（每次迭代必须执行）

完成功能后，在 **`CHANGELOG.md`** 的 `## [未发布]` 条目**之后**追加：

```markdown
## <简短标题> — YYYY-MM-DD

### 新增
- 具体功能

### 改进
- 具体改进

### 修复
- 问题描述（原因 → 修复方案）
```

**必须记录**：新 API 端点、Bug 修复、架构变更、部署配置修改、影响外部行为的重构。  
**无需记录**：仅改注释、纯格式化、临时调试代码清理。

---

## 文档索引

| 文档 | 路径 |
|------|------|
| 所有文档分类索引 | `docs/DOCUMENTATION_INDEX.md` |
| 架构设计 | `docs/architecture/ARCHITECTURE.md` |
| 快速入门 | `docs/guides/GETTING_STARTED.md` |
| API 参考 | `docs/guides/QUICK_REFERENCE.md` |
| 部署指南 | `docs/deployment/DEPLOYMENT_GUIDE.md` |
| 功能说明 | `docs/features/` |
| 问题修复记录 | `docs/fixes/` |
| 迭代变更历史 | `CHANGELOG.md` |

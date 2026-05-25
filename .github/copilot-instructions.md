# KnowledgeBaseService — 项目协作规则

## 项目概述

基于 **Qdrant + DeepSeek + C# .NET 8** 的 RAG 知识库服务，支持 15 种文件格式导入、向量检索、文档版本管理和对话长期记忆。

## 架构

4 层 Clean Architecture，依赖方向单向向下：

```
Api → Application → Infrastructure → Core
```

- **Core**：领域实体、常量，不依赖任何外部库
- **Infrastructure**：向量数据库（Qdrant）、LLM 客户端（Ark API）、PostgreSQL 仓储
- **Application**：业务逻辑服务（RAG、文档、版本管理、记忆）
- **Api**：控制器、中间件、DI 注册

跨层传递数据只能使用 `DTOs/`，不得将实体类暴露到 Application 层以上。

## 构建与测试

```bash
dotnet build
dotnet test
dotnet run --project KnowledgeBaseService.Api
```

## 代码规范

- 所有 API 路由使用小写（`LowercaseUrls = true`）
- JSON 序列化使用 camelCase，反序列化大小写不敏感
- 异步方法必须有 `Async` 后缀
- 禁止在 Controller 层写业务逻辑，通过 Service 接口调用
- 新增功能须在 `KnowledgeBaseService.Application/Interfaces/` 先定义接口

## 文档结构

```
docs/
├── architecture/   架构设计文档
├── deployment/     部署指南
├── features/       功能说明
├── fixes/          问题修复记录
├── guides/         使用指南
├── reports/        项目报告
├── testing/        测试文档
└── version-management/  版本管理
```

完整文档索引：[docs/DOCUMENTATION_INDEX.md](../docs/DOCUMENTATION_INDEX.md)

## ⚠️ CHANGELOG 更新规则（必须执行）

**每次完成迭代后，必须在 `CHANGELOG.md` 顶部追加本次变更记录。**

### 格式模板

```markdown
## <简短标题> — YYYY-MM-DD

### 新增
- ...

### 改进
- ...

### 修复
- ...
```

### 判断标准

以下情况必须更新 CHANGELOG：
- 新增任何 API 端点或功能模块
- 修复 Bug（不论大小）
- 修改架构或依赖关系
- 更新部署配置（Docker、数据库迁移等）
- 对外部行为有影响的重构

以下情况不需要更新：
- 仅修改注释或文档
- 代码格式化（无逻辑变更）
- 调试临时代码的清理

### 位置

在 `CHANGELOG.md` 文件中，新记录插入在 `## [未发布]` 条目**之后**、上一条记录**之前**。

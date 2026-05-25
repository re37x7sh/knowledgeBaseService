# CHANGELOG

所有重要变更均记录于此文件。格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)。

---

## [未发布]

## Docker 配置脱敏 — 2026-05-25

### 改进
- Docker Compose 改为通过环境变量读取 LLM、Redis、PostgreSQL 配置，避免在仓库中提交真实凭据。
- 部署文档和 `.env.example` 使用占位符示例，移除硬编码敏感信息。

---

## [1.0.0] — 2025-01-15

### 新增
- 完整 RAG 知识库系统（Qdrant + DeepSeek + C# .NET 8）
- 21 个 REST API 端点，100% 完成
- 15 种文件格式导入支持

---

## Phase 20 — 2025-01-15

### 修复
- 非流式 RAG 查询响应错误（`response.data.data` 重复访问）
- TypeScript 类型定义修正，添加 `CustomAxiosInstance` 自定义 Axios 类型
- 响应拦截器逻辑完善，影响所有 API 调用

---

## Phase 8 — 2025-11-25

### 新增
- **豆包视觉模型集成**（`doubao-seed-1-6-vision-250815`）：图片内容识别与文字提取
- **PPT 智能处理**：LibreOffice 逐页转 PNG → 视觉识别 → 内容合并
- **文件格式扩展**至 15 种：图片（PNG/JPG/JPEG/BMP/GIF 共 5 种）+ PPT/PPTX（1 种）

### 改进
- Docker 镜像集成 LibreOffice（~420MB）
- Alpine Linux 补充构建工具（python3、make、g++、libc6-compat）
- 使用 `npm ci` 替代 `npm install` 提升构建稳定性，`--legacy-peer-deps` 处理依赖冲突
- 图片和 PPT 内容可进行完整 RAG 语义检索

---

## Phase 12 — 2024-12-20

### 新增
- 版本创建**双模式 UI**（前端 Vue3）
  - 编辑模式：预加载当前版本内容直接修改
  - 上传模式：拖拽上传文件 + 自动提取标题

### 修复
- 后端 JSON 序列化配置（camelCase + 大小写不敏感反序列化）
- 路由小写化配置（`LowercaseUrls = true`）

### 验证
- 集成测试通过 10/10 API 端点
- 后端编译 0 错误，前端 Vite 构建 0 错误
- 整体成熟度 97/100

---

## Phase 7 — 版本管理集成审计

### 新增
- 版本管理完整审计报告（95/100 成熟度）
- 10/10 API 端点覆盖验证（获取列表、创建、对比、回滚、标签、删除、统计、导出）

### 修复
- 后端返回 PascalCase 而前端期望 camelCase → 添加 camelCase 序列化配置
- 路由 `/DocumentVersions` 与 `/documentversions` 大小写不一致 → `LowercaseUrls` 配置

---

## 混合模式（Hybrid Mode）— 2025-11-24

### 新增
- RAG 查询**混合模式**：优先知识库回答，不足时补充通用知识（与默认严格模式可切换）
- 系统提示词策略：`BuildStrictModeSystemPrompt()` / `BuildHybridModeSystemPrompt()`
- 前端混合模式开关 UI，`enableHybridMode` 参数前后端全链路传递

---

## 版本管理工作流优化

### 改进
- 文档列表页集成版本管理入口按钮（方案 A：集成式设计）
- 版本管理页面添加返回按钮，优化页面导航体验

---

## 历史文档格式修复

### 修复
- 添加 `FileExtension` 字段之前导入的历史文档缺少该属性
- 提供批量修复接口 `UpdateFileExtensionAsync()` 从文件名自动推断并回填扩展名

# Knowledge Base Service - Vue3 Web UI

一个基于 Vue3 + TypeScript + Vite 的现代化知识库管理系统前端。支持文档导入、进度跟踪、RAG 查询和对话功能。

## ✨ 主要功能

### 📄 文档管理
- **单文件导入**: 支持 Word(.docx)、PDF、Markdown(.md) 格式
- **批量导入**: 一次导入最多 10 个文件，实时显示进度
- **文档列表**: 查看、搜索、分类文档，分页展示
- **进度追踪**: 实时显示上传和索引进度

### 💬 RAG 查询与对话
- **语义搜索**: 基于向量相似度的智能搜索
- **流式响应**: 支持实时流式生成答案
- **来源追溯**: 显示相关文档和相关度评分
- **对话历史**: 完整的对话记录管理

## 🚀 快速开始

### 前置要求
- Node.js >= 18.0
- npm 或 yarn

### 安装依赖

```bash
cd KnowledgeBaseService.Web
npm install
```

### 开发模式

```bash
npm run dev
```

访问 http://localhost:5173

### 生产构建

```bash
npm run build
```

## 📦 项目结构

```
src/
├── api/              # API 调用模块
│   ├── client.ts     # Axios 实例配置
│   ├── document.ts   # 文档 API
│   └── rag.ts        # RAG 查询 API
├── components/       # 可复用组件
│   ├── FileImport.vue    # 文件导入组件
│   ├── DocumentList.vue  # 文档列表组件
│   └── RAGChat.vue       # RAG 对话组件
├── stores/           # Pinia 状态管理
│   ├── document.ts   # 文档状态
│   └── chat.ts       # 对话状态
├── types/            # TypeScript 类型定义
│   ├── document.ts   # 文档相关类型
│   └── rag.ts        # RAG 查询类型
├── views/            # 页面组件
│   └── MainLayout.vue    # 主布局
├── router/           # 路由配置
│   └── index.ts
├── App.vue           # 根组件
└── main.ts           # 应用入口
```

## 🔧 技术栈

- **框架**: Vue 3 (Composition API)
- **构建工具**: Vite
- **语言**: TypeScript
- **UI 组件**: Element Plus
- **状态管理**: Pinia
- **路由**: Vue Router 4
- **HTTP 客户端**: Axios
- **CSS**: Scoped CSS + Element Plus

## 📡 API 集成

### 后端服务连接

开发模式下，所有 `/api` 请求会自动代理到 `http://localhost:5000`。

在 `vite.config.ts` 中配置代理：

```typescript
server: {
  proxy: {
    '/api': {
      target: 'http://localhost:5000',
      changeOrigin: true,
    }
  }
}
```

### 支持的 API 端点

**文档管理**:
- `GET /api/documents/supported-formats` - 获取支持的文件格式
- `POST /api/documents/import-from-file` - 单文件导入
- `POST /api/documents/import-files-batch` - 批量导入
- `GET /api/documents/list` - 获取文档列表
- `GET /api/documents/{id}` - 获取文档详情
- `DELETE /api/documents/{id}` - 删除文档

**RAG 查询**:
- `POST /api/rag/query` - 执行查询
- `POST /api/rag/query-stream` - 流式查询

## 💡 使用示例

### 1. 导入文档

前往"导入文档"页面：
- 拖拽或点击上传文件
- 输入文档标题和分类
- 点击"开始导入"
- 实时监控导入和索引进度

### 2. 浏览文档

在"文档列表"页面：
- 搜索、筛选文档
- 查看文档详情
- 对文档执行查询
- 删除不需要的文档

### 3. RAG 查询

在"RAG 对话"页面：
- 输入自然语言问题
- 选择是否使用流式响应
- 查看答案和相关源文档
- 保存对话历史

## 🎨 UI 特性

- 🎭 现代化设计，响应式布局
- ✨ 流畅的动画和过渡效果
- 📱 移动友好的界面
- ♿ 无障碍访问支持
- 🌙 深色模式支持（可扩展）

## 🔐 安全性

- CSRF 保护
- 输入验证和清理
- 文件上传限制（50MB 单文件，10 文件批量）
- 错误消息不暴露敏感信息

## 📊 性能优化

- 代码分割和懒加载
- 虚拟滚动处理大列表
- 防抖和节流函数调用
- 缓存优化策略

## 🐛 故障排除

### 导入失败
- 检查文件格式是否正确（.docx, .pdf, .md）
- 确认文件大小未超过 50MB
- 查看浏览器控制台错误信息

### 查询无结果
- 确认文档已成功导入
- 检查向量索引是否完成
- 尝试使用不同的搜索词

### 连接后端失败
- 确保后端服务运行在 http://localhost:5000
- 检查防火墙和代理设置
- 查看浏览器网络标签页

## 📝 环境变量

创建 `.env` 文件（可选）：

```env
VITE_API_BASE_URL=http://localhost:5000
VITE_API_TIMEOUT=30000
```

在代码中使用：

```typescript
const apiUrl = import.meta.env.VITE_API_BASE_URL || '/api'
```

## 🚀 部署

### Docker 部署

创建 `Dockerfile`：

```dockerfile
FROM node:18-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
```

### 环境变量配置

```bash
npm run build
npm run preview
```

## 🤝 贡献指南

欢迎提交 Issue 和 Pull Request！

## 📄 许可证

MIT License

## 📞 支持

如有问题或建议，请提交 Issue 或联系项目维护者。

---

**最后更新**: 2025-11-16
**版本**: 1.0.0

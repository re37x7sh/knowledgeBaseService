# 环境配置指南

## 开发环境设置

### 1. Node.js 安装

```bash
# 使用 NVM (推荐)
nvm install 18
nvm use 18

# 或直接下载
# https://nodejs.org/en/
```

### 2. 依赖安装

```bash
cd KnowledgeBaseService.Web
npm install
```

如果遇到依赖问题，尝试清除缓存：

```bash
npm cache clean --force
rm -rf node_modules package-lock.json
npm install
```

### 3. 开发服务器启动

```bash
npm run dev
```

服务器将启动在 http://localhost:5173

### 4. 后端服务配置

确保后端 .NET 服务运行在 http://localhost:5000：

```bash
cd ../KnowledgeBaseService.Api
dotnet run
```

## 生产环境构建

```bash
# 生成最小化的生产包
npm run build

# 预览生产包
npm run preview

# 输出目录: dist/
```

## 类型检查

```bash
# 运行 TypeScript 类型检查
npm run type-check
```

## 常见问题

### 问题 1: 端口已被占用

```bash
# 修改 vite.config.ts 中的端口
server: {
  port: 5174
}
```

### 问题 2: 模块不存在

```bash
# 清除 node_modules 并重新安装
rm -rf node_modules
npm install
```

### 问题 3: 代理连接失败

确保 http://localhost:5000 后端服务运行正常。

## 浏览器支持

- Chrome >= 87
- Firefox >= 78
- Safari >= 14
- Edge >= 87

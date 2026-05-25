# 从 Node 镜像构建前端应用
FROM node:18-alpine AS builder

WORKDIR /app

# 安装构建依赖（Alpine Linux 需要这些）
RUN apk add --no-cache python3 make g++ libc6-compat

# 复制依赖文件
COPY package*.json ./

# 清除 npm 缓存并安装依赖（使用 --legacy-peer-deps 避免依赖冲突）
RUN npm cache clean --force && \
    npm install --legacy-peer-deps && \
    npm ci

# 复制源代码
COPY . .

# 构建应用
RUN npm run build

# 使用 Nginx 提供前端应用
FROM nginx:alpine

# 复制构建产物
COPY --from=builder /app/dist /usr/share/nginx/html

# 复制 Nginx 配置
COPY nginx.conf /etc/nginx/nginx.conf

# 暴露端口
EXPOSE 80

# 启动 Nginx
CMD ["nginx", "-g", "daemon off;"]

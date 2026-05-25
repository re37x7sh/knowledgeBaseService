# Docker Compose 操作指南

## 前置条件

- 已安装 Docker 和 Docker Compose
- 已配置 `.env` 环境变量文件
- 已在服务器上启动了 Redis 和 PostgreSQL

## 环境准备

```bash
# 进入项目目录
cd /var/www/KnowledgeBaseService

# 查看 .env 文件配置
cat .env

# 必需的环境变量
# DEEPSEEK_API_KEY=your-api-key
# REDIS_HOST=host.docker.internal 或 localhost
# REDIS_PORT=6379
# DB_CONNECTION_STRING=Host=xxx;Port=5432;...
```

---

## 基础操作

### 1. 启动所有服务（生产部署）

```bash
# 后台启动所有容器（Qdrant、API、Web）
docker-compose -f docker/docker-compose.yml up -d

# 验证容器启动状态
docker-compose -f docker/docker-compose.yml ps
```

**预期输出：**
```
NAME                    STATUS
qdrant_db              Up
knowledge_base_api     Up
knowledge_base_web     Up
```

---

### 2. 查看实时日志

```bash
# 查看所有容器日志
docker-compose -f docker/docker-compose.yml logs -f

# 查看特定容器日志
docker logs -f qdrant_db              # Qdrant 日志
docker logs -f knowledge_base_api     # API 日志
docker logs -f knowledge_base_web     # Web 日志

# 查看最后 100 行日志
docker logs --tail 100 knowledge_base_api
```

---

### 3. 停止所有容器（保留数据）

```bash
# 停止所有容器
docker-compose -f docker/docker-compose.yml stop

# 验证容器已停止
docker-compose -f docker/docker-compose.yml ps
```

---

### 4. 重启所有容器

```bash
# 重启所有服务
docker-compose -f docker/docker-compose.yml restart

# 重启特定容器
docker-compose -f docker/docker-compose.yml restart qdrant
docker-compose -f docker/docker-compose.yml restart api
docker-compose -f docker/docker-compose.yml restart web
```

---

### 5. 完全清理（删除容器、不删除数据）

```bash
# 停止并删除所有容器
docker-compose -f docker/docker-compose.yml down

# 删除容器后重新启动
docker-compose -f docker/docker-compose.yml up -d
```

---

## 构建操作

### 6. 构建单个服务

```bash
# 构建 API 服务（C# 后端）
docker-compose -f docker/docker-compose.yml build api

# 构建 Web 服务（Vue3 前端）
docker-compose -f docker/docker-compose.yml build web

# 构建所有服务
docker-compose -f docker/docker-compose.yml build
```

---

### 7. 无缓存重新构建（用于修复问题）

```bash
# 无缓存构建 API（清除之前的构建）
docker-compose -f docker/docker-compose.yml build --no-cache api

# 无缓存构建 Web
docker-compose -f docker/docker-compose.yml build --no-cache web

# 无缓存构建并启动
docker-compose -f docker/docker-compose.yml up -d --build
```

---

### 8. 修改后重新部署

```bash
# 场景：修改了代码后重新部署

# 仅 API 更改
docker-compose -f docker/docker-compose.yml build --no-cache api
docker-compose -f docker/docker-compose.yml restart api

# 仅 Web 更改
docker-compose -f docker/docker-compose.yml build --no-cache web
docker-compose -f docker/docker-compose.yml restart web

# 两者都改
docker-compose -f docker/docker-compose.yml build --no-cache
docker-compose -f docker/docker-compose.yml restart
```

---

## 容器进入和调试

### 9. 进入容器内部

```bash
# 进入 API 容器
docker exec -it knowledge_base_api /bin/sh

# 进入 Web 容器
docker exec -it knowledge_base_web /bin/sh

# 进入 Qdrant 容器
docker exec -it qdrant_db /bin/sh
```

---

### 10. 在容器内执行命令

```bash
# 在 API 容器中执行 curl 命令测试健康检查
docker exec knowledge_base_api curl http://localhost/health

# 在 Web 容器中查看 Nginx 配置
docker exec knowledge_base_web cat /etc/nginx/nginx.conf

# 在 Qdrant 容器中查看日志
docker exec qdrant_db cat /var/log/qdrant.log
```

---

## 检查和验证

### 11. 检查容器状态和详细信息

```bash
# 查看容器详细信息（包括网络、卷、环境变量）
docker inspect knowledge_base_api

# 查看容器资源使用情况
docker stats knowledge_base_api

# 检查容器网络配置
docker network inspect knowledge-base-network
```

---

### 12. 端口和连接检查

```bash
# 查看容器的端口映射
docker port knowledge_base_api
docker port knowledge_base_web

# 从宿主机测试 API 连接
curl http://localhost:5000/health

# 从宿主机测试 Web 连接
curl http://localhost:8080/health
```

---

## 数据和卷管理

### 13. 查看卷信息

```bash
# 列出所有卷
docker volume ls

# 查看 Qdrant 卷详细信息
docker volume inspect qdrant_storage

# 查看卷的物理路径（Linux）
docker volume inspect qdrant_storage | grep Mountpoint
```

---

### 14. 清理数据

```bash
# 删除卷（谨慎！会删除 Qdrant 数据）
docker volume rm qdrant_storage

# 完全清理：删除所有容器、卷和网络（谨慎操作！）
docker-compose -f docker/docker-compose.yml down -v
```

---

## 故障排查

### 15. 常见问题解决

```bash
# 问题：容器无法启动或健康检查失败

# 1. 查看容器日志
docker logs knowledge_base_api

# 2. 检查容器是否真的在运行
docker ps -a

# 3. 检查端口是否被占用
sudo netstat -tlnp | grep 5000
sudo netstat -tlnp | grep 8080
sudo netstat -tlnp | grep 6333

# 4. 检查防火墙
sudo firewall-cmd --list-all
sudo firewall-cmd --permanent --add-port=5000/tcp
sudo firewall-cmd --reload

# 5. 清理并重新启动
docker-compose -f docker/docker-compose.yml down
docker-compose -f docker/docker-compose.yml up -d
```

---

## 性能和监控

### 16. 实时监控

```bash
# 实时查看所有容器的资源使用
docker stats

# 查看特定容器的资源使用
docker stats knowledge_base_api knowledge_base_web

# 查看容器的详细事件日志
docker events --filter container=knowledge_base_api
```

---

### 17. 镜像管理

```bash
# 列出所有镜像
docker images

# 查看特定镜像信息
docker image inspect knowledge-base-service-api:latest

# 删除镜像（可选）
docker rmi knowledge-base-service-api:latest
docker rmi knowledge-base-service-web:latest

# 清理未使用的镜像
docker image prune -a
```

---

## 部署工作流

### 18. 完整部署流程

```bash
# 1. 进入项目目录
cd /var/www/KnowledgeBaseService

# 2. 验证 .env 配置
cat .env

# 3. 构建镜像
docker-compose -f docker/docker-compose.yml build --no-cache

# 4. 启动服务
docker-compose -f docker/docker-compose.yml up -d

# 5. 验证所有容器运行
docker-compose -f docker/docker-compose.yml ps

# 6. 检查 API 健康
curl http://localhost:5000/health

# 7. 检查 Web 健康
curl http://localhost:8080

# 8. 查看日志确认无错误
docker-compose -f docker/docker-compose.yml logs
```

---

### 19. 更新后重新部署

```bash
# 场景：更新了代码或配置

# 1. 停止旧服务
docker-compose -f docker/docker-compose.yml down

# 2. 构建新镜像
docker-compose -f docker/docker-compose.yml build --no-cache

# 3. 启动新服务
docker-compose -f docker/docker-compose.yml up -d

# 4. 验证
docker-compose -f docker/docker-compose.yml ps
```

---

## 环境变量参考

### 20. .env 配置示例

```bash
# 深搜 API 密钥（Ark 模型）
DEEPSEEK_API_KEY=your-ark-api-key-here

# Redis 配置
REDIS_HOST=host.docker.internal
REDIS_PORT=6379

# PostgreSQL 连接字符串
DB_CONNECTION_STRING=Host=host.docker.internal;Port=5432;Database=knowledge_base;Username=postgres;Password=your-password-here

# Qdrant API Key（可选）
QDRANT_API_KEY=
```

---

## 快速参考

```bash
# 最常用命令

# 启动所有服务
docker-compose -f docker/docker-compose.yml up -d

# 停止所有服务
docker-compose -f docker/docker-compose.yml stop

# 查看状态
docker-compose -f docker/docker-compose.yml ps

# 查看日志
docker-compose -f docker/docker-compose.yml logs -f

# 重新部署（修改后）
docker-compose -f docker/docker-compose.yml build --no-cache && docker-compose -f docker/docker-compose.yml up -d

# 清理一切重新开始
docker-compose -f docker/docker-compose.yml down -v && docker-compose -f docker/docker-compose.yml up -d
```

---

## 常见端口

| 服务 | 容器内端口 | 宿主机端口 | 说明 |
|------|-----------|----------|------|
| Qdrant | 6333 | 6333 | 向量数据库 HTTP |
| Qdrant | 6334 | 6334 | 向量数据库 gRPC |
| API | 80 | 5000 | 后端 API |
| Web | 80 | 8080 | 前端应用 |

# 1. 无缓存构建所有容器
docker-compose -f docker/docker-compose.yml build --no-cache

# 2. 启动所有容器（后台运行）
docker-compose -f docker/docker-compose.yml up -d

# 3. 验证容器启动状态
docker-compose -f docker/docker-compose.yml ps

# 4. 查看API日志（检查是否有错误）
docker logs -f knowledge_base_api

---

**更新时间**：2025年11月21日
**项目**：知识库 RAG 系统

# Docker Compose 部署指南

本文档详细介绍知识库服务系统的 Docker Compose 部署命令和使用方法。

---

## 📋 目录

- [前置要求](#前置要求)
- [项目架构](#项目架构)
- [基础命令](#基础命令)
- [构建命令](#构建命令)
- [启动与停止](#启动与停止)
- [服务管理](#服务管理)
- [日志查看](#日志查看)
- [数据管理](#数据管理)
- [故障排查](#故障排查)
- [生产环境部署](#生产环境部署)

---

## 前置要求

### 必需软件

```bash
# 1. Docker Desktop（Windows/Mac）或 Docker Engine（Linux）
# 版本要求：Docker 20.10+ / Docker Compose 2.0+
docker --version
# 输出示例：Docker version 24.0.6

# 2. Docker Compose
docker-compose --version
# 输出示例：Docker Compose version v2.23.0

# 3. Git（用于克隆代码）
git --version
```

### 系统资源建议

| 资源类型 | 最低配置 | 推荐配置 |
|---------|---------|---------|
| **CPU** | 2 核 | 4 核+ |
| **内存** | 4 GB | 8 GB+ |
| **磁盘** | 10 GB | 20 GB+ |
| **网络** | 稳定网络连接 | 高速网络 |

---

## 项目架构

### 服务组成

```
┌─────────────────────────────────────────────┐
│           知识库服务系统                     │
├─────────────────────────────────────────────┤
│                                             │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐    │
│  │   Web   │  │   API   │  │ Qdrant  │    │
│  │  :8080  │←→│  :5000  │←→│  :6333  │    │
│  │ Nginx   │  │ .NET 8  │  │ Vector  │    │
│  └─────────┘  └─────────┘  └─────────┘    │
│       ↓             ↓             ↓         │
│  [前端资源]    [业务逻辑]   [向量存储]      │
│                     ↓                       │
│              ┌─────────────┐               │
│              │ PostgreSQL  │               │
│              │   外部数据库  │               │
│              └─────────────┘               │
│                     ↓                       │
│              ┌─────────────┐               │
│              │    Redis    │               │
│              │   外部缓存   │               │
│              └─────────────┘               │
└─────────────────────────────────────────────┘
```

### 容器列表

| 容器名称 | 服务名 | 端口映射 | 说明 |
|---------|--------|---------|------|
| `knowledge_base_web` | web | 8080:80 | 前端 Vue3 应用 |
| `knowledge_base_api` | api | 5000:80 | 后端 .NET 8 API |
| `qdrant_db` | qdrant | 6333:6333, 6334:6334 | 向量数据库 |

### Volume 持久化

| Volume 名称 | 挂载路径 | 用途 |
|------------|---------|------|
| `qdrant_storage` | /qdrant/storage | Qdrant 向量数据 |
| `uploaded_images` | /app/wwwroot/uploads/images | 上传的图片文件 |
| `uploaded_files` | /app/wwwroot/uploads | 其他上传文件 |

---

## 基础命令

### 1. 查看帮助信息

```bash
# 查看所有可用命令
docker-compose --help

# 查看特定命令的帮助（如 up 命令）
docker-compose up --help
```

**说明**：
- `--help` 显示命令的详细用法和选项
- 每个子命令都有自己的帮助文档

---

### 2. 验证配置文件

```bash
# 验证 docker-compose.yml 语法是否正确
docker-compose -f docker/docker-compose.yml config

# 输出解析后的完整配置（包括环境变量替换）
docker-compose -f docker/docker-compose.yml config --services
```

**说明**：
- `-f` 指定配置文件路径
- `config` 验证并显示最终配置
- `--services` 只显示服务名称列表
- 在修改配置文件后，建议先验证再部署

**输出示例**：
```yaml
services:
  api:
    build:
      context: ..
      dockerfile: docker/Dockerfile
    container_name: knowledge_base_api
    ...
```

---

## 构建命令

### 1. 首次完整构建（推荐）

```bash
# 清除缓存，从头开始构建所有服务
docker-compose -f docker/docker-compose.yml build --no-cache

# 或者使用完整路径（从项目根目录执行）
cd d:\dev\KnowledgeBaseService
docker-compose -f docker/docker-compose.yml build --no-cache
```

**参数说明**：
- `-f docker/docker-compose.yml`：指定配置文件位置
- `build`：构建镜像命令
- `--no-cache`：不使用构建缓存，确保使用最新依赖

**适用场景**：
- ✅ 首次部署
- ✅ Dockerfile 有重大修改
- ✅ 依赖包版本需要更新（如 NuGet、npm）
- ✅ 遇到奇怪的构建问题

**预计时间**：5-10 分钟（取决于网络速度）

**输出示例**：
```
[+] Building 245.3s (23/23) FINISHED
 => [api internal] load build definition from Dockerfile
 => => transferring dockerfile: 2.34kB
 => [api] FROM mcr.microsoft.com/dotnet/aspnet:8.0
 => [api] RUN apt-get update && apt-get install -y libreoffice
 ...
 => [api] exporting to image
 => => naming to docker.io/library/docker-api
```

---

### 2. 增量构建（日常开发）

```bash
# 使用缓存进行快速构建
docker-compose -f docker/docker-compose.yml build
```

**参数说明**：
- 无 `--no-cache`：利用 Docker 层缓存
- 只重新构建修改过的层

**适用场景**：
- ✅ 代码有小幅修改
- ✅ 配置文件微调
- ✅ 日常开发迭代

**预计时间**：30 秒 - 2 分钟

---

### 3. 并行构建（加速）

```bash
# 同时构建多个服务，加快速度
docker-compose -f docker/docker-compose.yml build --parallel
```

**参数说明**：
- `--parallel`：并行构建所有服务
- 多核 CPU 可显著提速

**适用场景**：
- ✅ 多个服务同时修改
- ✅ 首次构建加速
- ✅ CPU 核心数 ≥ 4

**注意事项**：
- ⚠️ 输出日志会交错，不便调试
- ⚠️ 内存占用会增加

---

### 4. 构建单个服务

```bash
# 只构建 API 服务
docker-compose -f docker/docker-compose.yml build api

# 只构建前端服务
docker-compose -f docker/docker-compose.yml build web

# 只构建 Qdrant（通常不需要，因为使用官方镜像）
docker-compose -f docker/docker-compose.yml build qdrant
```

**参数说明**：
- 最后的参数是服务名（定义在 docker-compose.yml 中）

**适用场景**：
- ✅ 只修改了某个服务的代码
- ✅ 快速迭代开发
- ✅ 节省构建时间

**预计时间**：10-60 秒（取决于修改内容）

---

### 5. 查看构建进度（详细模式）

```bash
# 显示详细的构建日志
docker-compose -f docker/docker-compose.yml build --progress=plain

# 输出所有 RUN 命令的详细信息
docker-compose -f docker/docker-compose.yml build --progress=plain --no-cache
```

**参数说明**：
- `--progress=plain`：纯文本输出，显示完整日志
- 默认是 `auto`（简洁的进度条）

**适用场景**：
- ✅ 调试构建失败问题
- ✅ 查看依赖安装过程
- ✅ 排查 Dockerfile 错误

---

### 6. 拉取官方镜像

```bash
# 拉取所有服务的基础镜像
docker-compose -f docker/docker-compose.yml pull

# 只拉取 Qdrant 镜像
docker-compose -f docker/docker-compose.yml pull qdrant
```

**参数说明**：
- `pull`：从 Docker Hub 拉取镜像
- 不会构建本地 Dockerfile

**适用场景**：
- ✅ 更新官方镜像到最新版本
- ✅ 首次部署前预先下载镜像
- ✅ 网络良好时提前拉取

---

## 启动与停止

### 1. 启动所有服务（前台模式）

```bash
# 前台运行，实时查看日志
docker-compose -f docker/docker-compose.yml up
```

**参数说明**：
- `up`：创建并启动容器
- 前台模式：日志输出到终端
- 按 `Ctrl + C` 停止服务

**适用场景**：
- ✅ 开发调试时实时查看日志
- ✅ 快速测试
- ✅ 首次启动验证

**输出示例**：
```
[+] Running 3/3
 ✔ Container qdrant_db              Started
 ✔ Container knowledge_base_api     Started
 ✔ Container knowledge_base_web     Started
Attaching to qdrant_db, knowledge_base_api, knowledge_base_web
qdrant_db | [INFO] Starting Qdrant...
api       | [INFO] Application started at http://+:80
web       | [INFO] Nginx started on port 80
```

---

### 2. 启动所有服务（后台模式）★★★

```bash
# 后台运行（推荐用于生产环境）
docker-compose -f docker/docker-compose.yml up -d
```

**参数说明**：
- `-d` / `--detach`：后台运行（daemon 模式）
- 容器在后台持续运行
- 终端可以关闭，服务不受影响

**适用场景**：
- ✅ 生产环境部署
- ✅ 长期运行服务
- ✅ 服务器部署

**验证服务状态**：
```bash
# 查看运行中的容器
docker-compose -f docker/docker-compose.yml ps

# 输出示例
NAME                    STATUS          PORTS
qdrant_db               Up 2 minutes    0.0.0.0:6333->6333/tcp
knowledge_base_api      Up 2 minutes    0.0.0.0:5000->80/tcp
knowledge_base_web      Up 2 minutes    0.0.0.0:8080->80/tcp
```

---

### 3. 构建并启动（一键部署）★★★

```bash
# 构建镜像 + 启动服务（最常用）
docker-compose -f docker/docker-compose.yml up --build -d

# 无缓存构建 + 启动
docker-compose -f docker/docker-compose.yml up --build --no-cache -d
```

**参数说明**：
- `--build`：启动前先构建镜像
- `-d`：后台运行

**适用场景**：
- ✅ 代码更新后重新部署
- ✅ 一键部署脚本
- ✅ CI/CD 流水线

**等价于**：
```bash
docker-compose -f docker/docker-compose.yml build
docker-compose -f docker/docker-compose.yml up -d
```

---

### 4. 启动单个服务

```bash
# 只启动 API 服务（会自动启动依赖的 qdrant）
docker-compose -f docker/docker-compose.yml up -d api

# 只启动前端服务（会自动启动依赖的 api 和 qdrant）
docker-compose -f docker/docker-compose.yml up -d web

# 强制只启动指定服务，不启动依赖
docker-compose -f docker/docker-compose.yml up -d --no-deps web
```

**参数说明**：
- 最后的参数是服务名
- `--no-deps`：不启动依赖服务

**适用场景**：
- ✅ 单独重启某个服务
- ✅ 调试特定服务

---

### 5. 停止所有服务

```bash
# 停止所有容器（保留容器）
docker-compose -f docker/docker-compose.yml stop

# 停止并删除容器（保留镜像和 Volume）
docker-compose -f docker/docker-compose.yml down
```

**参数说明**：
- `stop`：停止容器，但不删除
- `down`：停止 + 删除容器和网络

**区别说明**：

| 命令 | 停止容器 | 删除容器 | 删除网络 | 删除 Volume | 删除镜像 |
|------|---------|---------|---------|------------|---------|
| `stop` | ✅ | ❌ | ❌ | ❌ | ❌ |
| `down` | ✅ | ✅ | ✅ | ❌ | ❌ |
| `down -v` | ✅ | ✅ | ✅ | ✅ | ❌ |
| `down --rmi all` | ✅ | ✅ | ✅ | ❌ | ✅ |

**适用场景**：
- `stop`：临时停止，准备重启
- `down`：完全清理，准备重新部署

---

### 6. 重启服务

```bash
# 重启所有服务
docker-compose -f docker/docker-compose.yml restart

# 重启单个服务
docker-compose -f docker/docker-compose.yml restart api

# 重启并设置超时时间（默认 10 秒）
docker-compose -f docker/docker-compose.yml restart -t 30 api
```

**参数说明**：
- `restart`：停止 + 启动容器
- `-t` / `--timeout`：停止超时时间（秒）

**适用场景**：
- ✅ 配置文件修改后应用
- ✅ 服务异常需要重启
- ✅ 内存泄漏临时缓解

---

### 7. 完全清理（危险操作）

```bash
# 停止并删除所有容器、网络、Volume、镜像
docker-compose -f docker/docker-compose.yml down -v --rmi all

# 只删除 Volume（会丢失所有数据）
docker-compose -f docker/docker-compose.yml down -v
```

**参数说明**：
- `-v` / `--volumes`：删除所有 Volume（⚠️ 数据会丢失）
- `--rmi all`：删除所有构建的镜像
- `--rmi local`：只删除没有 tag 的镜像

**警告**：
- ⚠️ **会删除所有数据库数据**
- ⚠️ **会删除所有上传的图片文件**
- ⚠️ **不可恢复**

**适用场景**：
- ✅ 完全重新部署
- ✅ 清理开发环境
- ❌ 生产环境禁用

---

## 服务管理

### 1. 查看服务状态

```bash
# 查看所有服务状态
docker-compose -f docker/docker-compose.yml ps

# 查看所有容器（包括停止的）
docker-compose -f docker/docker-compose.yml ps -a

# 只显示服务名
docker-compose -f docker/docker-compose.yml ps --services
```

**输出示例**：
```
NAME                    IMAGE           STATUS          PORTS
qdrant_db               qdrant:latest   Up 10 minutes   0.0.0.0:6333->6333/tcp
knowledge_base_api      docker-api      Up 10 minutes   0.0.0.0:5000->80/tcp
knowledge_base_web      docker-web      Up 10 minutes   0.0.0.0:8080->80/tcp
```

**状态说明**：
- `Up`：运行中
- `Exited (0)`：正常退出
- `Exited (1)`：异常退出
- `Restarting`：重启中

---

### 2. 查看服务资源占用

```bash
# 实时显示 CPU、内存、网络、磁盘 IO
docker stats knowledge_base_api knowledge_base_web qdrant_db

# 只显示一次（不实时刷新）
docker stats --no-stream knowledge_base_api knowledge_base_web qdrant_db
```

**输出示例**：
```
CONTAINER           CPU %   MEM USAGE / LIMIT     MEM %   NET I/O
knowledge_base_api  5.23%   345.2MiB / 8GiB      4.23%   12.4MB / 8.3MB
knowledge_base_web  0.12%   45.8MiB / 8GiB       0.56%   1.2MB / 890KB
qdrant_db           2.45%   512.3MiB / 8GiB      6.28%   8.7MB / 12.1MB
```

---

### 3. 进入容器内部

```bash
# 进入 API 容器的 bash
docker exec -it knowledge_base_api bash

# 如果没有 bash，使用 sh
docker exec -it knowledge_base_api sh

# 执行单个命令（不进入交互模式）
docker exec knowledge_base_api ls -la /app

# 查看上传的图片文件
docker exec knowledge_base_api ls -la /app/wwwroot/uploads/images
```

**参数说明**：
- `-i` / `--interactive`：保持 STDIN 打开
- `-t` / `--tty`：分配伪终端
- `bash` / `sh`：要执行的 shell

**常用调试命令**：
```bash
# 进入容器后可以执行
cd /app                          # 进入应用目录
ls -la                           # 查看文件
cat appsettings.json             # 查看配置
ps aux                           # 查看进程
netstat -tulpn                   # 查看端口
exit                             # 退出容器
```

---

### 4. 暂停与恢复服务

```bash
# 暂停所有服务（进程冻结，不占用 CPU）
docker-compose -f docker/docker-compose.yml pause

# 恢复所有服务
docker-compose -f docker/docker-compose.yml unpause

# 暂停单个服务
docker-compose -f docker/docker-compose.yml pause api
```

**参数说明**：
- `pause`：冻结容器的所有进程
- `unpause`：恢复进程运行

**适用场景**：
- ✅ 临时释放 CPU 资源
- ✅ 调试其他服务
- ✅ 内存不足时临时暂停

**区别说明**：
- `pause`：进程暂停，内存保留
- `stop`：进程终止，资源释放

---

### 5. 缩放服务实例

```bash
# 将 API 服务扩展到 3 个实例（负载均衡）
docker-compose -f docker/docker-compose.yml up -d --scale api=3

# 将 Web 服务扩展到 2 个实例
docker-compose -f docker/docker-compose.yml up -d --scale web=2
```

**参数说明**：
- `--scale 服务名=实例数`：设置实例数量

**注意事项**：
- ⚠️ 需要移除 `container_name`（否则会冲突）
- ⚠️ 需要配置负载均衡器（如 Nginx）
- ⚠️ 端口映射可能冲突

---

## 日志查看

### 1. 查看所有服务日志

```bash
# 查看所有服务的日志
docker-compose -f docker/docker-compose.yml logs

# 实时跟踪日志（类似 tail -f）
docker-compose -f docker/docker-compose.yml logs -f

# 只显示最后 100 行
docker-compose -f docker/docker-compose.yml logs --tail=100

# 实时跟踪最后 50 行
docker-compose -f docker/docker-compose.yml logs -f --tail=50
```

**参数说明**：
- `logs`：查看历史日志
- `-f` / `--follow`：实时跟踪新日志
- `--tail=N`：只显示最后 N 行

**适用场景**：
- ✅ 排查启动失败问题
- ✅ 监控运行状态
- ✅ 调试业务逻辑

---

### 2. 查看单个服务日志

```bash
# 查看 API 服务日志
docker-compose -f docker/docker-compose.yml logs api

# 实时跟踪 API 日志
docker-compose -f docker/docker-compose.yml logs -f api

# 查看 Qdrant 日志
docker-compose -f docker/docker-compose.yml logs -f qdrant

# 查看前端日志
docker-compose -f docker/docker-compose.yml logs -f web
```

---

### 3. 带时间戳的日志

```bash
# 显示日志时间戳
docker-compose -f docker/docker-compose.yml logs -f --timestamps

# 输出示例
api  | 2025-01-15T08:30:45.123456789Z [INFO] Application started
web  | 2025-01-15T08:30:46.234567890Z [INFO] Nginx listening on port 80
```

**参数说明**：
- `--timestamps` / `-t`：显示每条日志的时间戳

---

### 4. 过滤日志（按时间）

```bash
# 查看最近 1 小时的日志
docker-compose -f docker/docker-compose.yml logs --since 1h

# 查看最近 30 分钟的日志
docker-compose -f docker/docker-compose.yml logs --since 30m

# 查看指定时间之后的日志
docker-compose -f docker/docker-compose.yml logs --since "2025-01-15T08:00:00"

# 查看指定时间范围的日志
docker-compose -f docker/docker-compose.yml logs --since "2025-01-15T08:00:00" --until "2025-01-15T09:00:00"
```

**参数说明**：
- `--since`：显示指定时间之后的日志
- `--until`：显示指定时间之前的日志

**时间格式**：
- `1h`：1 小时前
- `30m`：30 分钟前
- `2025-01-15T08:00:00`：ISO 8601 格式

---

### 5. 保存日志到文件

```bash
# 保存所有日志到文件
docker-compose -f docker/docker-compose.yml logs > logs/all_services.log

# 保存 API 日志到文件
docker-compose -f docker/docker-compose.yml logs api > logs/api.log

# 保存最近 1000 行日志
docker-compose -f docker/docker-compose.yml logs --tail=1000 > logs/recent.log
```

**适用场景**：
- ✅ 归档日志
- ✅ 离线分析
- ✅ 提交 bug 报告

---

### 6. 使用 Docker 原生命令查看日志

```bash
# 查看容器日志（更多高级选项）
docker logs knowledge_base_api

# 实时跟踪
docker logs -f knowledge_base_api

# 显示最后 100 行
docker logs --tail=100 knowledge_base_api

# 查看最近 1 小时的日志
docker logs --since 1h knowledge_base_api

# 带时间戳
docker logs -f --timestamps knowledge_base_api
```

---

## 数据管理

### 1. 查看 Volume 列表

```bash
# 查看所有 Docker Volume
docker volume ls

# 过滤本项目的 Volume
docker volume ls | grep knowledge

# 输出示例
local     docker_qdrant_storage
local     docker_uploaded_images
local     docker_uploaded_files
```

---

### 2. 查看 Volume 详细信息

```bash
# 查看 Qdrant 数据卷信息
docker volume inspect docker_qdrant_storage

# 查看上传图片卷信息
docker volume inspect docker_uploaded_images

# 输出示例（JSON 格式）
[
    {
        "CreatedAt": "2025-01-15T08:30:45Z",
        "Driver": "local",
        "Labels": {
            "com.docker.compose.project": "docker",
            "com.docker.compose.volume": "qdrant_storage"
        },
        "Mountpoint": "/var/lib/docker/volumes/docker_qdrant_storage/_data",
        "Name": "docker_qdrant_storage",
        "Scope": "local"
    }
]
```

**关键信息**：
- `Mountpoint`：Volume 在宿主机的实际路径
- `CreatedAt`：创建时间
- `Labels`：关联的项目和服务

---

### 3. 备份 Volume 数据

```bash
# 备份 Qdrant 数据
docker run --rm -v docker_qdrant_storage:/data -v ${PWD}/backups:/backup alpine tar czf /backup/qdrant_$(date +%Y%m%d_%H%M%S).tar.gz -C /data .

# 备份上传的图片
docker run --rm -v docker_uploaded_images:/data -v ${PWD}/backups:/backup alpine tar czf /backup/images_$(date +%Y%m%d_%H%M%S).tar.gz -C /data .

# PowerShell 版本（Windows）
docker run --rm -v docker_qdrant_storage:/data -v ${PWD}/backups:/backup alpine tar czf /backup/qdrant_backup.tar.gz -C /data .
```

**命令解释**：
- `--rm`：备份完成后自动删除临时容器
- `-v docker_qdrant_storage:/data`：挂载要备份的 Volume
- `-v ${PWD}/backups:/backup`：挂载备份目标目录
- `alpine`：轻量级 Linux 镜像
- `tar czf`：创建压缩包
- `$(date +%Y%m%d_%H%M%S)`：时间戳文件名

---

### 4. 恢复 Volume 数据

```bash
# 恢复 Qdrant 数据
docker run --rm -v docker_qdrant_storage:/data -v ${PWD}/backups:/backup alpine tar xzf /backup/qdrant_20250115_083045.tar.gz -C /data

# 恢复图片数据
docker run --rm -v docker_uploaded_images:/data -v ${PWD}/backups:/backup alpine tar xzf /backup/images_20250115_083045.tar.gz -C /data
```

**注意事项**：
- ⚠️ 恢复前先停止服务：`docker-compose down`
- ⚠️ 确认备份文件完整性
- ⚠️ 恢复后重启服务：`docker-compose up -d`

---

### 5. 清理未使用的 Volume

```bash
# 清理所有未使用的 Volume（⚠️ 危险）
docker volume prune

# 强制清理，不提示确认
docker volume prune -f

# 清理指定 Volume
docker volume rm docker_qdrant_storage
```

**警告**：
- ⚠️ `prune` 会删除所有未关联容器的 Volume
- ⚠️ 数据不可恢复
- ⚠️ 生产环境慎用

---

### 6. 复制文件到/从容器

```bash
# 从容器复制文件到宿主机
docker cp knowledge_base_api:/app/appsettings.json ./config/

# 从宿主机复制文件到容器
docker cp ./config/appsettings.Production.json knowledge_base_api:/app/

# 复制整个目录
docker cp knowledge_base_api:/app/wwwroot/uploads ./backups/uploads/
```

**适用场景**：
- ✅ 导出配置文件
- ✅ 备份上传文件
- ✅ 临时修改配置

---

## 故障排查

### 1. 容器启动失败

```bash
# 步骤 1：查看容器状态
docker-compose -f docker/docker-compose.yml ps -a

# 步骤 2：查看容器日志
docker-compose -f docker/docker-compose.yml logs api

# 步骤 3：查看容器详细信息
docker inspect knowledge_base_api

# 步骤 4：检查退出代码
docker ps -a --filter "name=knowledge_base_api"
```

**常见退出代码**：
- `Exit 0`：正常退出
- `Exit 1`：应用异常
- `Exit 137`：内存不足被 OOM Killer 杀死
- `Exit 139`：段错误（Segmentation fault）

---

### 2. 端口冲突

```bash
# 检查端口占用（Windows PowerShell）
netstat -ano | findstr :5000
netstat -ano | findstr :8080
netstat -ano | findstr :6333

# 查找占用进程
Get-Process -Id <PID>

# 终止占用进程
Stop-Process -Id <PID> -Force

# 或修改 docker-compose.yml 中的端口映射
# 将 5000:80 改为 5001:80
```

---

### 3. 网络问题

```bash
# 查看 Docker 网络
docker network ls

# 查看网络详细信息
docker network inspect docker_knowledge-base-network

# 测试容器间网络连通性
docker exec knowledge_base_api ping qdrant
docker exec knowledge_base_web ping api

# 重建网络
docker-compose -f docker/docker-compose.yml down
docker-compose -f docker/docker-compose.yml up -d
```

---

### 4. 磁盘空间不足

```bash
# 查看 Docker 磁盘占用
docker system df

# 输出示例
TYPE            TOTAL     ACTIVE    SIZE      RECLAIMABLE
Images          15        3         5.2GB     3.8GB (73%)
Containers      5         3         1.2GB     500MB (41%)
Local Volumes   8         3         2.5GB     1.8GB (72%)
Build Cache     45        0         1.5GB     1.5GB (100%)

# 清理未使用的资源
docker system prune -a

# 只清理构建缓存
docker builder prune -a

# 清理所有未使用的镜像
docker image prune -a
```

---

### 5. 内存不足

```bash
# 查看容器内存使用
docker stats --no-stream

# 限制容器内存（在 docker-compose.yml 中添加）
# services:
#   api:
#     deploy:
#       resources:
#         limits:
#           memory: 2G
#         reservations:
#           memory: 512M

# 重启释放内存
docker-compose -f docker/docker-compose.yml restart
```

---

### 6. 配置文件错误

```bash
# 验证配置文件语法
docker-compose -f docker/docker-compose.yml config

# 如果有错误，输出会提示具体位置
# 例如：
# ERROR: yaml.parser.ParserError: while parsing a block mapping
#   in "./docker-compose.yml", line 15, column 3
```

---

### 7. 健康检查

```bash
# 查看服务健康状态（如果配置了 healthcheck）
docker inspect --format='{{.State.Health.Status}}' knowledge_base_api

# 查看健康检查日志
docker inspect --format='{{json .State.Health}}' knowledge_base_api | jq

# 手动测试服务是否可访问
curl http://localhost:5000/health
curl http://localhost:8080
curl http://localhost:6333/collections
```

---

## 生产环境部署

### 1. 环境变量配置

```bash
# 创建 .env 文件（敏感信息不提交到 Git）
cat > .env <<EOF
# 数据库连接
DATABASE_HOST=192.168.1.21
DATABASE_PORT=5432
DATABASE_NAME=knowledge_base
DATABASE_USER=postgres
DATABASE_PASSWORD=your-password-here

# Redis 连接
REDIS_HOST=host.docker.internal
REDIS_PORT=6379

# DeepSeek API
DEEPSEEK_API_KEY=your_api_key_here
DEEPSEEK_BASE_URL=https://ark.cn-beijing.volces.com

# Qdrant API（可选）
QDRANT_API_KEY=your_qdrant_key_here
EOF

# 在 docker-compose.yml 中使用
# environment:
#   - ConnectionStrings__DefaultConnection=Host=${DATABASE_HOST};Port=${DATABASE_PORT}...
```

---

### 2. 生产环境启动脚本

创建 `deploy.sh`（Linux/Mac）或 `deploy.ps1`（Windows）：

```bash
#!/bin/bash
# deploy.sh

set -e  # 遇到错误立即退出

echo "🚀 开始部署知识库服务..."

# 1. 拉取最新代码
echo "📦 拉取最新代码..."
git pull origin main

# 2. 停止旧服务
echo "⏹️  停止旧服务..."
docker-compose -f docker/docker-compose.yml down

# 3. 备份数据
echo "💾 备份数据..."
mkdir -p backups
docker run --rm -v docker_qdrant_storage:/data -v ${PWD}/backups:/backup alpine tar czf /backup/qdrant_$(date +%Y%m%d_%H%M%S).tar.gz -C /data .

# 4. 构建新镜像
echo "🔨 构建新镜像..."
docker-compose -f docker/docker-compose.yml build --no-cache

# 5. 启动新服务
echo "▶️  启动新服务..."
docker-compose -f docker/docker-compose.yml up -d

# 6. 等待服务启动
echo "⏳ 等待服务启动..."
sleep 10

# 7. 健康检查
echo "🏥 健康检查..."
curl -f http://localhost:5000/health || echo "⚠️  API 健康检查失败"
curl -f http://localhost:8080 || echo "⚠️  Web 健康检查失败"
curl -f http://localhost:6333 || echo "⚠️  Qdrant 健康检查失败"

# 8. 显示服务状态
echo "📊 服务状态："
docker-compose -f docker/docker-compose.yml ps

echo "✅ 部署完成！"
```

**PowerShell 版本 (deploy.ps1)**：

```powershell
# deploy.ps1

Write-Host "🚀 开始部署知识库服务..." -ForegroundColor Green

# 1. 拉取最新代码
Write-Host "📦 拉取最新代码..." -ForegroundColor Yellow
git pull origin main

# 2. 停止旧服务
Write-Host "⏹️  停止旧服务..." -ForegroundColor Yellow
docker-compose -f docker/docker-compose.yml down

# 3. 备份数据
Write-Host "💾 备份数据..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path backups
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
docker run --rm -v docker_qdrant_storage:/data -v ${PWD}/backups:/backup alpine tar czf /backup/qdrant_$timestamp.tar.gz -C /data .

# 4. 构建新镜像
Write-Host "🔨 构建新镜像..." -ForegroundColor Yellow
docker-compose -f docker/docker-compose.yml build --no-cache

# 5. 启动新服务
Write-Host "▶️  启动新服务..." -ForegroundColor Yellow
docker-compose -f docker/docker-compose.yml up -d

# 6. 等待服务启动
Write-Host "⏳ 等待服务启动..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

# 7. 健康检查
Write-Host "🏥 健康检查..." -ForegroundColor Yellow
try {
    Invoke-WebRequest -Uri "http://localhost:5000/health" -UseBasicParsing | Out-Null
    Write-Host "✅ API 健康检查通过" -ForegroundColor Green
} catch {
    Write-Host "⚠️  API 健康检查失败" -ForegroundColor Red
}

# 8. 显示服务状态
Write-Host "📊 服务状态：" -ForegroundColor Yellow
docker-compose -f docker/docker-compose.yml ps

Write-Host "✅ 部署完成！" -ForegroundColor Green
```

**使用方式**：
```bash
# Linux/Mac
chmod +x deploy.sh
./deploy.sh

# Windows PowerShell
.\deploy.ps1
```

---

### 3. 日志轮转（避免日志占满磁盘）

在 `docker-compose.yml` 中添加日志配置：

```yaml
services:
  api:
    # ... 其他配置
    logging:
      driver: "json-file"
      options:
        max-size: "10m"      # 单个日志文件最大 10MB
        max-file: "3"        # 最多保留 3 个日志文件
```

---

### 4. 监控与告警

```bash
# 使用 Docker Events 监控容器状态
docker events --filter 'container=knowledge_base_api' --filter 'event=die'

# 配合 Prometheus + Grafana 监控（需要额外配置）
# 或使用 Docker 原生监控工具
docker stats --format "table {{.Container}}\t{{.CPUPerc}}\t{{.MemUsage}}"
```

---

### 5. 自动重启策略

```yaml
services:
  api:
    # ... 其他配置
    restart: unless-stopped  # 容器异常退出时自动重启
    # 可选值：
    # - no: 不自动重启
    # - always: 总是重启
    # - on-failure: 只在失败时重启
    # - unless-stopped: 除非手动停止，否则总是重启
```

---

## 常用组合命令

### 1. 完整部署流程（从零开始）

```bash
# 克隆代码
git clone <repository_url>
cd KnowledgeBaseService

# 配置环境变量
cp .env.example .env
# 编辑 .env 文件，填写真实配置

# 构建并启动
docker-compose -f docker/docker-compose.yml up --build -d

# 查看日志
docker-compose -f docker/docker-compose.yml logs -f
```

---

### 2. 日常更新部署

```bash
# 拉取代码
git pull

# 重新构建并启动
docker-compose -f docker/docker-compose.yml up --build -d

# 查看服务状态
docker-compose -f docker/docker-compose.yml ps
```

---

### 3. 快速重启（不重新构建）

```bash
# 重启所有服务
docker-compose -f docker/docker-compose.yml restart

# 或停止后启动
docker-compose -f docker/docker-compose.yml stop
docker-compose -f docker/docker-compose.yml start
```

---

### 4. 调试单个服务

```bash
# 停止要调试的服务
docker-compose -f docker/docker-compose.yml stop api

# 删除容器
docker-compose -f docker/docker-compose.yml rm -f api

# 重新构建并启动（前台模式查看日志）
docker-compose -f docker/docker-compose.yml up --build api
```

---

### 5. 完全清理并重新部署

```bash
# ⚠️ 警告：会删除所有数据

# 停止并删除所有容器、网络、Volume
docker-compose -f docker/docker-compose.yml down -v

# 清理所有镜像
docker-compose -f docker/docker-compose.yml down --rmi all

# 清理系统
docker system prune -a -f

# 重新构建并启动
docker-compose -f docker/docker-compose.yml up --build -d
```

---

## 快速参考表

| 操作 | 命令 |
|------|------|
| **构建镜像** | `docker-compose -f docker/docker-compose.yml build` |
| **首次构建** | `docker-compose -f docker/docker-compose.yml build --no-cache` |
| **启动服务（后台）** | `docker-compose -f docker/docker-compose.yml up -d` |
| **启动服务（前台）** | `docker-compose -f docker/docker-compose.yml up` |
| **构建并启动** | `docker-compose -f docker/docker-compose.yml up --build -d` |
| **停止服务** | `docker-compose -f docker/docker-compose.yml stop` |
| **停止并删除** | `docker-compose -f docker/docker-compose.yml down` |
| **重启服务** | `docker-compose -f docker/docker-compose.yml restart` |
| **查看状态** | `docker-compose -f docker/docker-compose.yml ps` |
| **查看日志** | `docker-compose -f docker/docker-compose.yml logs -f` |
| **查看单服务日志** | `docker-compose -f docker/docker-compose.yml logs -f api` |
| **进入容器** | `docker exec -it knowledge_base_api bash` |
| **查看资源占用** | `docker stats` |
| **验证配置** | `docker-compose -f docker/docker-compose.yml config` |
| **完全清理** | `docker-compose -f docker/docker-compose.yml down -v --rmi all` |

---

## 故障排查检查清单

遇到问题时，按顺序检查：

- [ ] 1. 查看服务状态：`docker-compose ps`
- [ ] 2. 查看容器日志：`docker-compose logs <service>`
- [ ] 3. 检查端口占用：`netstat -ano | findstr :5000`
- [ ] 4. 检查磁盘空间：`docker system df`
- [ ] 5. 检查网络连通性：`docker exec api ping qdrant`
- [ ] 6. 检查配置文件：`docker-compose config`
- [ ] 7. 查看容器详情：`docker inspect <container>`
- [ ] 8. 进入容器调试：`docker exec -it <container> bash`
- [ ] 9. 重启服务：`docker-compose restart`
- [ ] 10. 重建服务：`docker-compose up --build -d`

---

## 联系与支持

如果遇到问题，请提供以下信息：

```bash
# 收集诊断信息
docker --version
docker-compose --version
docker-compose -f docker/docker-compose.yml config
docker-compose -f docker/docker-compose.yml ps
docker-compose -f docker/docker-compose.yml logs --tail=100 > logs/debug.log
```

---

**文档版本**: v1.0.0  
**最后更新**: 2025-01-15  
**维护者**: Knowledge Base Service Team

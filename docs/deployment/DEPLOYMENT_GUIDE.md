# 部署指南 - DocumentVersionService 数据库持久化

## 前置条件
1. PostgreSQL 15+ 运行在 `192.168.1.21:5432`
2. 数据库 `knowledge_base` 已创建，用户 `lucifer` 有完整权限
3. Docker 已安装在服务器（如需容器部署）

## 快速部署步骤

### 步骤 1：停止现有服务
```bash
cd /path/to/docker
docker compose down
```

### 步骤 2：清理旧镜像（可选但推荐）
```bash
docker rmi knowledge_base_api  # 移除旧后端镜像
```

### 步骤 3：重新构建并启动
```bash
docker compose up -d --build
```

此命令将：
1. 从源代码重新构建后端 Docker 镜像
2. 拉取/启动所有服务（Qdrant、API、Web）
3. SqlSugar CodeFirst 自动创建 DocumentVersion 表

### 步骤 4：验证部署
```bash
# 查看后端日志
docker compose logs -f api

# 等待看到以下消息表示成功
# "Application started. Press Ctrl+C to shut down."
```

## 验证功能

### 测试 1：创建带版本的文档
```bash
curl -X POST http://192.168.1.21:5000/api/documents/import-file \
  -F "file=@test.txt" \
  -F "category=Test"

# 预期返回：201 Created
# 响应体包含 DocumentId
```

### 测试 2：查询版本列表
```bash
curl http://192.168.1.21:5000/api/documentversions/document/{DocumentId}

# 预期返回：200 OK
# 响应体示例：
# [
#   {
#     "id": "uuid-1",
#     "versionNumber": 1,
#     "title": "test.txt",
#     "tag": "initial",
#     "createdAt": "2024-...",
#     "isCurrent": true
#   }
# ]
```

### 测试 3：再次导入同一文档，验证版本递增
```bash
# 重复测试 1 的导入
# 然后查询版本，应显示 2 个版本

curl http://192.168.1.21:5000/api/documentversions/document/{DocumentId}

# 应返回版本号 1 和 2
```

### 测试 4：容器重启持久化检验（最关键）
```bash
# 重启后端容器
docker compose restart api

# 等待启动完成
sleep 10

# 查询同一文档的版本
curl http://192.168.1.21:5000/api/documentversions/document/{DocumentId}

# 预期：版本数据完全保留，不是空列表
```

## 故障排查

### 问题：启动时报数据库连接错误
```
Exception: Unable to connect to Postgres at 192.168.1.21:5432
```
**解决**：
1. 确认 PostgreSQL 服务运行：`docker ps | grep postgres`
2. 检查网络连接：`docker compose exec api ping 192.168.1.21`
3. 验证数据库凭证（用户/密码）

### 问题：版本端点返回 404
```
404 Not Found: /api/documentversions/document/{id}
```
**解决**：
1. 文档必须存在：确认 DocumentId 正确
2. 重新导入文档自动创建版本
3. 检查 API 日志：`docker compose logs api | grep -i version`

### 问题：导入文档后没有创建版本
```
DocumentResponse 返回，但版本端点返回空
```
**解决**：
1. 查看服务日志是否有版本创建错误
2. 手动验证数据库：
   ```sql
   SELECT * FROM "DocumentVersion" WHERE document_id = 'uuid';
   ```
3. 如无行记录，说明版本创建失败

## 数据库检查

### 连接数据库并验证表
```bash
# 进入容器内的 shell（非必需，示例）
# 或使用本地 psql 客户端

psql -h 192.168.1.21 -U lucifer -d knowledge_base

# 查询表结构
\d "DocumentVersion"

# 查询现有版本
SELECT id, document_id, version_number, title, is_current, created_at 
FROM "DocumentVersion" 
ORDER BY created_at DESC 
LIMIT 10;
```

## 回滚计划（如出现严重问题）

### 回滚到上一个版本
```bash
cd /path/to/docker

# 停止当前服务
docker compose down

# 恢复旧镜像（如已备份）
docker tag knowledge_base_api:backup knowledge_base_api:latest

# 重启
docker compose up -d
```

### 清空版本表重新开始
```bash
# 警告：此操作删除所有版本数据！

psql -h 192.168.1.21 -U lucifer -d knowledge_base -c "TRUNCATE TABLE \"DocumentVersion\";"
```

## 监控和日志

### 实时查看日志
```bash
docker compose logs -f --tail=100 api
```

### 导出日志用于分析
```bash
docker compose logs api > api.log 2>&1
```

### 查看特定日期的日志
```bash
docker compose logs --since "2024-01-15" api
```

## 性能优化建议

### 1. 添加数据库索引（可选）
```sql
CREATE INDEX idx_docversion_document_id ON "DocumentVersion"(document_id);
CREATE INDEX idx_docversion_version_number ON "DocumentVersion"(document_id, version_number);
CREATE INDEX idx_docversion_created_at ON "DocumentVersion"(created_at);
```

### 2. 配置连接池
编辑 appsettings.json：
```json
"ConnectionStrings": {
  "DefaultConnection": "Host=host.docker.internal;Port=5432;Database=knowledge_base;Username=postgres;Password=your-password-here;Maximum Pool Size=20;Minimum Pool Size=5;"
}
```

## 数据迁移（仅当有旧数据时）

如有之前在内存中存储的版本数据需要迁移：
1. 导出旧数据（JSON 或其他格式）
2. 创建数据迁移脚本
3. 执行迁移到 PostgreSQL
4. 验证数据完整性

联系开发团队获取迁移脚本。

## 支持和反馈

部署过程中如有问题：
1. 检查上述故障排查部分
2. 查看 `MIGRATION_COMPLETE.md` 了解技术细节
3. 检查容器日志获取详细错误信息

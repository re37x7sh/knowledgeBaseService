# 豆包（ByteDance Ark）API 集成指南

## 概述

本项目已从 DeepSeek 迁移到豆包（ByteDance Ark）作为 AI 模型提供商，使用豆包的向量嵌入和对话模型。

## 使用的模型

### 向量嵌入模型
- **模型名称**: `doubao-embedding-text-240715`
- **维度**: 1024
- **用途**: 将文本转换为向量进行语义搜索
- **API 端点**: `https://ark.cn-beijing.volces.com/api/v3/embeddings`

### 对话模型
- **模型名称**: `doubao-1-5-pro-32k-250115`
- **上下文长度**: 32K tokens
- **用途**: 基于检索文档生成回答
- **API 端点**: `https://ark.cn-beijing.volces.com/api/v3/chat/completions`

## 获取 API Key

1. 访问 [火山引擎控制台](https://console.volcengine.com)
2. 登录或注册账户
3. 创建新项目
4. 在 "API 密钥" 中创建新的 API Key
5. 复制 API Key 并配置到环境变量

## 环境配置

### .env 文件配置

```bash
# 豆包 API Key
DEEPSEEK_API_KEY=pat-xxx...

# Redis 连接
REDIS_HOST=host.docker.internal
REDIS_PORT=6379

# PostgreSQL 连接
DB_CONNECTION_STRING=Host=host.docker.internal;Port=5432;Database=knowledge_base;Username=lucifer;Password=your-password
```

### appsettings.json 配置

```json
{
  "DeepSeek": {
    "ApiKey": "${DEEPSEEK_API_KEY}",
    "BaseUrl": "https://ark.cn-beijing.volces.com"
  }
}
```

## API 请求格式

### 向量嵌入请求

```bash
curl https://ark.cn-beijing.volces.com/api/v3/embeddings \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $ARK_API_KEY" \
  -d '{
    "model": "doubao-embedding-text-240715",
    "input": [
      "文本 1",
      "文本 2"
    ],
    "encoding_format": "float"
  }'
```

### 对话请求

```bash
curl https://ark.cn-beijing.volces.com/api/v3/chat/completions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $ARK_API_KEY" \
  -d '{
    "model": "doubao-1-5-pro-32k-250115",
    "messages": [
      {
        "role": "system",
        "content": "You are a helpful assistant."
      },
      {
        "role": "user",
        "content": "Hello!"
      }
    ],
    "temperature": 0.7,
    "max_tokens": 1024,
    "stream": false
  }'
```

### 流式对话请求

```bash
curl https://ark.cn-beijing.volces.com/api/v3/chat/completions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $ARK_API_KEY" \
  -d '{
    "model": "doubao-1-5-pro-32k-250115",
    "messages": [...],
    "stream": true
  }'
```

## 实现细节

### DeepSeekEmbeddingClient.cs

负责处理向量嵌入请求，使用豆包的嵌入模型将文本转换为 1024 维的向量。

**主要方法**:
- `GetEmbeddingAsync(string text)` - 获取单个文本的向量
- `GetEmbeddingsAsync(List<string> texts)` - 批量获取向量

### DeepSeekChatClient.cs

负责处理对话请求，支持单次请求和流式响应。

**主要方法**:
- `GetCompletionAsync(...)` - 单次对话
- `GetCompletionStreamAsync(...)` - 流式对话

## 响应格式

### 向量嵌入响应

```json
{
  "data": [
    {
      "index": 0,
      "embedding": [0.123, 0.456, ...],
      "object": "embedding"
    }
  ],
  "object": "list",
  "model": "doubao-embedding-text-240715",
  "usage": {
    "prompt_tokens": 10,
    "total_tokens": 10
  }
}
```

### 对话响应

```json
{
  "id": "chatcmpl-xxx",
  "object": "chat.completion",
  "created": 1234567890,
  "model": "doubao-1-5-pro-32k-250115",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "回答内容..."
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 20,
    "completion_tokens": 30,
    "total_tokens": 50
  }
}
```

### 流式对话响应

```
data: {"choices":[{"delta":{"content":"流"},"index":0}],"model":"doubao-1-5-pro-32k-250115"}
data: {"choices":[{"delta":{"content":"式"},"index":0}],"model":"doubao-1-5-pro-32k-250115"}
data: [DONE]
```

## 常见问题

### Q: API Key 格式是什么？
A: 豆包 API Key 通常以 `pat-` 开头，或其他火山引擎生成的 Key 格式。

### Q: 支持哪些语言？
A: 豆包模型支持中文、英文等多语言。

### Q: 如何处理 API 限流？
A: 项目中已实现重试逻辑和错误处理，可根据响应的 `429` 状态码或 `Retry-After` 头实现退避策略。

### Q: 向量维度是多少？
A: `doubao-embedding-text-240715` 模型输出 1024 维的向量。

## 文档参考

- [豆包文档中心](https://www.volcengine.com/docs/82379)
- [豆包 API 错误代码](https://www.volcengine.com/docs/82379/1098787)
- [API 限流说明](https://www.volcengine.com/docs/82379/1099300)

## 迁移检查清单

- [x] 替换 Embedding 客户端
- [x] 替换 Chat 客户端
- [x] 更新 API 端点 URL
- [x] 更新模型名称
- [x] 更新 .env 配置
- [x] 更新 appsettings.json
- [x] 测试向量嵌入功能
- [x] 测试对话功能
- [ ] 性能测试和调优
- [ ] 成本优化

## 支持

如有问题，请参考豆包官方文档或联系豆包支持团队。

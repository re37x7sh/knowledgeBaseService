# 长期记忆系统架构图

## 整体架构

```mermaid
graph TB
    subgraph "对话应用层"
        A[用户输入问题] --> B[检索长期记忆]
        B --> C[构建上下文]
        C --> D[调用 LLM]
        D --> E[生成回复]
        E --> F[保存对话记忆]
        F --> A
    end
    
    subgraph "知识库服务层"
        B --> G[Memory API]
        F --> G
        G --> H[ConversationMemoryService]
    end
    
    subgraph "数据存储层"
        H --> I[Qdrant<br/>向量检索]
        H --> J[PostgreSQL<br/>结构化存储]
        H --> K[Redis<br/>缓存]
    end
    
    style A fill:#e1f5ff
    style E fill:#c8e6c9
    style G fill:#fff9c4
    style I fill:#ffccbc
    style J fill:#d1c4e9
    style K fill:#f8bbd0
```

## 记忆保存流程

```mermaid
sequenceDiagram
    participant User as 用户
    participant App as 对话应用
    participant API as Memory API
    participant Service as MemoryService
    participant Embed as Embedding API
    participant Qdrant as Qdrant
    participant DB as PostgreSQL

    User->>App: 发送消息
    App->>API: POST /api/memory/save
    API->>Service: SaveMemoryAsync()
    Service->>Service: 提取摘要
    Service->>Embed: 生成向量
    Embed-->>Service: 返回 Embedding
    Service->>Qdrant: 存储向量 + Payload
    Service->>DB: 存储完整记忆
    DB-->>Service: 返回 MemoryId
    Service-->>API: 返回成功
    API-->>App: { memoryId }
    App->>User: 显示回复
```

## 记忆检索流程

```mermaid
sequenceDiagram
    participant User as 用户
    participant App as 对话应用
    participant API as Memory API
    participant Service as MemoryService
    participant Embed as Embedding API
    participant Qdrant as Qdrant
    participant DB as PostgreSQL

    User->>App: 提问
    App->>API: POST /api/memory/retrieve
    API->>Service: RetrieveMemoriesAsync()
    Service->>Embed: 向量化查询
    Embed-->>Service: Query Embedding
    Service->>Qdrant: 向量检索 (filter: userId)
    Qdrant-->>Service: Top-K 相似向量
    Service->>DB: 获取完整记忆信息
    DB-->>Service: 记忆详情列表
    Service->>Service: 计算综合得分<br/>(similarity × importance)
    Service-->>API: 返回排序后的记忆
    API-->>App: { memories: [...] }
    App->>App: 构建上下文
    App->>User: 使用记忆生成回复
```

## 数据模型关系

```mermaid
erDiagram
    ConversationMemory ||--o{ QdrantPoint : "has"
    ConversationMemory {
        string Id PK
        string UserId "索引"
        string SessionId
        string MemoryType
        string Summary
        text FullContent
        text VectorContent
        string VectorPointId FK
        double ImportanceScore
        int AccessCount
        datetime LastAccessedAt
        datetime CreatedAt
    }
    
    QdrantPoint {
        string PointId PK
        vector Embedding "2560维"
        json Payload
    }
    
    QdrantPayload {
        string memory_id
        string user_id
        string session_id
        string memory_type
        string summary
        string created_at
    }
    
    QdrantPoint ||--|| QdrantPayload : contains
```

## 记忆重要性计算

```mermaid
graph LR
    A[基础评分<br/>base_score] --> E[最终重要性<br/>ImportanceScore]
    B[时间衰减<br/>decay_multiplier] --> E
    C[访问强化<br/>access_boost] --> E
    
    D1[days_since_last_access] --> B
    D2[decay_factor_days] --> B
    D3[access_count] --> C
    
    E --> F{重要性阈值}
    F -->|高| G[保留记忆]
    F -->|低| H[淘汰记忆]
    
    style E fill:#ffd54f
    style G fill:#81c784
    style H fill:#e57373
```

## 用户隔离架构

```mermaid
graph TB
    subgraph "用户 A"
        UA[User A<br/>user_123] --> MA[记忆集合 A]
        MA --> QA[Qdrant Points<br/>filter: user_id=123]
        MA --> DA[DB Records<br/>WHERE UserId='123']
    end
    
    subgraph "用户 B"
        UB[User B<br/>user_456] --> MB[记忆集合 B]
        MB --> QB[Qdrant Points<br/>filter: user_id=456]
        MB --> DB[DB Records<br/>WHERE UserId='456']
    end
    
    subgraph "存储层（物理隔离通过过滤）"
        QA -.-> QdrantDB[(Qdrant<br/>conversation_memory_collection)]
        QB -.-> QdrantDB
        DA -.-> PostgresDB[(PostgreSQL<br/>ConversationMemory Table)]
        DB -.-> PostgresDB
    end
    
    style UA fill:#e3f2fd
    style UB fill:#fce4ec
    style QdrantDB fill:#fff3e0
    style PostgresDB fill:#e8f5e9
```

## 记忆类型分类

```mermaid
mindmap
  root((长期记忆))
    事实记忆 fact
      个人信息
        职业
        居住地
        兴趣爱好
      知识背景
        技能水平
        学习经历
    偏好记忆 preference
      交互偏好
        回答风格
        语言习惯
      内容偏好
        主题偏好
        详细程度
    上下文记忆 context
      对话历史
        讨论主题
        问答记录
      任务状态
        进行中任务
        历史任务
    任务记忆 task
      待办事项
        截止时间
        优先级
      提醒事项
        定期提醒
        一次性提醒
```

## 性能优化策略

```mermaid
graph TB
    A[查询请求] --> B{是否命中缓存?}
    B -->|是| C[返回缓存结果<br/>~5ms]
    B -->|否| D[向量检索]
    D --> E[批量数据库查询]
    E --> F[结果排序]
    F --> G[写入缓存]
    G --> H[返回结果<br/>~70ms]
    
    I[后台任务] --> J[预热热点记忆]
    I --> K[清理过期缓存]
    I --> L[更新重要性评分]
    
    style C fill:#81c784
    style H fill:#fff59d
    style I fill:#90caf9
```

## 与知识库集成

```mermaid
graph LR
    subgraph "对话上下文构建"
        A[用户问题] --> B[长期记忆检索]
        A --> C[知识库检索 RAG]
        A --> D[短期记忆<br/>会话历史]
        
        B --> E[个人记忆<br/>5条]
        C --> F[知识文档<br/>3条]
        D --> G[最近对话<br/>10轮]
        
        E --> H{上下文合并}
        F --> H
        G --> H
        
        H --> I[最终提示词]
    end
    
    I --> J[LLM 生成]
    J --> K[回复用户]
    
    style E fill:#ffccbc
    style F fill:#c5e1a5
    style G fill:#b3e5fc
    style I fill:#fff9c4
```

## 数据流时序图（完整流程）

```mermaid
sequenceDiagram
    participant U as 用户
    participant C as 对话应用
    participant M as 长期记忆服务
    participant R as RAG服务
    participant L as LLM

    U->>C: "帮我推荐一个Python框架"
    
    par 并行检索
        C->>M: 检索个人记忆
        M-->>C: [记忆1: 用户是后端工程师]<br/>[记忆2: 用户喜欢简洁代码]
    and
        C->>R: RAG 知识库检索
        R-->>C: [文档1: FastAPI介绍]<br/>[文档2: Django vs Flask]
    end
    
    C->>C: 构建上下文<br/>记忆 + 知识库 + 短期历史
    
    C->>L: 发送完整上下文
    L-->>C: "基于您是后端工程师的背景，<br/>推荐 FastAPI..."
    
    C->>M: 保存本次对话
    M-->>C: 保存成功
    
    C->>U: 显示回复
```

---

## 使用说明

这些 Mermaid 图表可以：

1. **在 Markdown 中直接渲染**（GitHub、VS Code、Typora 等）
2. **导出为图片**：使用 [Mermaid Live Editor](https://mermaid.live/)
3. **集成到文档**：添加到 README 或技术文档中

## 图表说明

| 图表 | 用途 |
|------|------|
| 整体架构 | 展示系统分层结构 |
| 记忆保存流程 | 详细的保存步骤 |
| 记忆检索流程 | 详细的检索步骤 |
| 数据模型关系 | ER 图，表结构关系 |
| 记忆重要性计算 | 淘汰策略逻辑 |
| 用户隔离架构 | 多用户数据隔离 |
| 记忆类型分类 | 思维导图，记忆分类 |
| 性能优化策略 | 缓存和批量查询 |
| 与知识库集成 | 多源上下文合并 |
| 完整数据流 | 端到端时序图 |

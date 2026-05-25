# 长期记忆集成示例代码

本文档提供多种编程语言的集成示例，方便快速集成到现有项目中。

---

## Python 示例

### 方案1: 简单封装

```python
import requests
from typing import List, Dict, Optional
from datetime import datetime

class ConversationMemory:
    """对话长期记忆客户端"""
    
    def __init__(self, user_id: str, base_url: str = "http://localhost:5000"):
        self.user_id = user_id
        self.base_url = base_url
        self.memory_api = f"{base_url}/api/memory"
    
    def save(
        self, 
        user_message: str, 
        assistant_message: str,
        memory_type: str = "context",
        importance: float = 0.5,
        session_id: Optional[str] = None
    ) -> str:
        """保存对话记忆"""
        response = requests.post(f"{self.memory_api}/save", json={
            "userId": self.user_id,
            "sessionId": session_id,
            "userMessage": user_message,
            "assistantMessage": assistant_message,
            "memoryType": memory_type,
            "importanceScore": importance
        })
        response.raise_for_status()
        return response.json()["memoryId"]
    
    def retrieve(
        self, 
        query: str, 
        top_k: int = 5,
        min_score: float = 0.6,
        memory_type: Optional[str] = None
    ) -> List[Dict]:
        """检索相关记忆"""
        response = requests.post(f"{self.memory_api}/retrieve", json={
            "userId": self.user_id,
            "query": query,
            "topK": top_k,
            "minScore": min_score,
            "memoryType": memory_type
        })
        response.raise_for_status()
        return response.json()["memories"]
    
    def get_recent(self, count: int = 10) -> List[Dict]:
        """获取最近记忆"""
        response = requests.get(
            f"{self.memory_api}/{self.user_id}/recent",
            params={"count": count}
        )
        response.raise_for_status()
        return response.json()["memories"]
    
    def cleanup(self, keep_top_n: int = 100):
        """清理低重要性记忆"""
        response = requests.post(
            f"{self.memory_api}/{self.user_id}/cleanup",
            params={"keepTopN": keep_top_n}
        )
        response.raise_for_status()
    
    def delete_all(self):
        """删除所有记忆"""
        response = requests.delete(f"{self.memory_api}/{self.user_id}")
        response.raise_for_status()


# 使用示例
memory = ConversationMemory(user_id="user_123")

# 保存记忆
memory.save(
    user_message="我是一名软件工程师",
    assistant_message="明白了，您是软件工程师。",
    memory_type="fact",
    importance=0.9
)

# 检索记忆
memories = memory.retrieve("推荐一个编程语言", top_k=5)
for m in memories:
    print(f"[{m['memoryType']}] {m['summary']} (相似度: {m['similarityScore']:.2f})")
```

### 方案2: 集成到 ChatBot

```python
import openai
from typing import List

class ChatBotWithMemory:
    """带长期记忆的聊天机器人"""
    
    def __init__(self, user_id: str, openai_api_key: str):
        self.user_id = user_id
        self.memory = ConversationMemory(user_id)
        self.conversation_history = []  # 短期记忆
        openai.api_key = openai_api_key
    
    def chat(self, user_message: str) -> str:
        """对话主流程"""
        # 1. 检索长期记忆
        long_term_memories = self.memory.retrieve(user_message, top_k=3)
        
        # 2. 构建完整上下文
        system_prompt = self._build_system_prompt(long_term_memories)
        
        # 3. 调用 OpenAI
        messages = [
            {"role": "system", "content": system_prompt},
            *self.conversation_history,  # 短期记忆
            {"role": "user", "content": user_message}
        ]
        
        response = openai.ChatCompletion.create(
            model="gpt-4",
            messages=messages
        )
        
        assistant_message = response.choices[0].message.content
        
        # 4. 更新短期记忆
        self.conversation_history.append({"role": "user", "content": user_message})
        self.conversation_history.append({"role": "assistant", "content": assistant_message})
        
        # 保留最近 10 轮对话
        if len(self.conversation_history) > 20:
            self.conversation_history = self.conversation_history[-20:]
        
        # 5. 保存到长期记忆
        importance = self._calculate_importance(user_message, assistant_message)
        self.memory.save(user_message, assistant_message, importance=importance)
        
        return assistant_message
    
    def _build_system_prompt(self, memories: List[Dict]) -> str:
        """构建系统提示词"""
        if not memories:
            return "你是一个智能助手，请友好地回答用户问题。"
        
        memory_context = "\n".join([
            f"- [{m['memoryType']}] {m['summary']}"
            for m in memories
        ])
        
        return f"""你是一个智能助手。以下是用户的历史记忆：

{memory_context}

请根据这些记忆个性化回复用户，但不要直接提及"记忆"。"""
    
    def _calculate_importance(self, user_msg: str, assistant_msg: str) -> float:
        """计算重要性（可以调用 LLM）"""
        # 简化版本：根据消息长度
        if len(user_msg) > 50 or "重要" in user_msg:
            return 0.8
        return 0.5


# 使用示例
bot = ChatBotWithMemory(user_id="user_123", openai_api_key="sk-...")

response1 = bot.chat("我是一名 Python 开发者")
print(response1)  # "很高兴认识您！作为 Python 开发者..."

response2 = bot.chat("帮我推荐一个 Web 框架")
print(response2)  # "基于您是 Python 开发者，推荐 FastAPI..."
```

### 方案3: 异步版本

```python
import aiohttp
import asyncio
from typing import List, Dict

class AsyncConversationMemory:
    """异步版本的记忆客户端"""
    
    def __init__(self, user_id: str, base_url: str = "http://localhost:5000"):
        self.user_id = user_id
        self.memory_api = f"{base_url}/api/memory"
    
    async def save(
        self, 
        user_message: str, 
        assistant_message: str,
        **kwargs
    ) -> str:
        async with aiohttp.ClientSession() as session:
            async with session.post(f"{self.memory_api}/save", json={
                "userId": self.user_id,
                "userMessage": user_message,
                "assistantMessage": assistant_message,
                **kwargs
            }) as resp:
                data = await resp.json()
                return data["memoryId"]
    
    async def retrieve(self, query: str, top_k: int = 5) -> List[Dict]:
        async with aiohttp.ClientSession() as session:
            async with session.post(f"{self.memory_api}/retrieve", json={
                "userId": self.user_id,
                "query": query,
                "topK": top_k
            }) as resp:
                data = await resp.json()
                return data["memories"]


# 使用示例
async def main():
    memory = AsyncConversationMemory("user_123")
    
    # 并发保存和检索
    tasks = [
        memory.save("消息1", "回复1"),
        memory.save("消息2", "回复2"),
        memory.retrieve("查询问题")
    ]
    results = await asyncio.gather(*tasks)
    print(results)

asyncio.run(main())
```

---

## TypeScript/Node.js 示例

### 方案1: 简单封装

```typescript
import axios, { AxiosInstance } from 'axios';

interface Memory {
  id: string;
  summary: string;
  fullContent: string;
  similarityScore: number;
  importanceScore: number;
  memoryType: string;
  createdAt: string;
}

interface SaveMemoryRequest {
  userId: string;
  sessionId?: string;
  userMessage: string;
  assistantMessage: string;
  memoryType?: string;
  importanceScore?: number;
}

interface RetrieveMemoryRequest {
  userId: string;
  query: string;
  topK?: number;
  minScore?: number;
  memoryType?: string;
}

class ConversationMemory {
  private client: AxiosInstance;
  private userId: string;

  constructor(userId: string, baseUrl: string = 'http://localhost:5000') {
    this.userId = userId;
    this.client = axios.create({
      baseURL: `${baseUrl}/api/memory`,
      headers: { 'Content-Type': 'application/json' }
    });
  }

  async save(
    userMessage: string,
    assistantMessage: string,
    options: Partial<SaveMemoryRequest> = {}
  ): Promise<string> {
    const response = await this.client.post('/save', {
      userId: this.userId,
      userMessage,
      assistantMessage,
      memoryType: options.memoryType || 'context',
      importanceScore: options.importanceScore || 0.5,
      sessionId: options.sessionId
    });
    return response.data.memoryId;
  }

  async retrieve(
    query: string,
    options: Partial<RetrieveMemoryRequest> = {}
  ): Promise<Memory[]> {
    const response = await this.client.post('/retrieve', {
      userId: this.userId,
      query,
      topK: options.topK || 5,
      minScore: options.minScore || 0.6,
      memoryType: options.memoryType
    });
    return response.data.memories;
  }

  async getRecent(count: number = 10): Promise<Memory[]> {
    const response = await this.client.get(`/${this.userId}/recent`, {
      params: { count }
    });
    return response.data.memories;
  }

  async cleanup(keepTopN: number = 100): Promise<void> {
    await this.client.post(`/${this.userId}/cleanup`, null, {
      params: { keepTopN }
    });
  }

  async deleteAll(): Promise<void> {
    await this.client.delete(`/${this.userId}`);
  }
}

// 使用示例
const memory = new ConversationMemory('user_123');

// 保存记忆
await memory.save(
  '我是一名软件工程师',
  '明白了，您是软件工程师。',
  { memoryType: 'fact', importanceScore: 0.9 }
);

// 检索记忆
const memories = await memory.retrieve('推荐一个编程语言', { topK: 5 });
memories.forEach(m => {
  console.log(`[${m.memoryType}] ${m.summary} (${m.similarityScore.toFixed(2)})`);
});
```

### 方案2: 集成到 Express 应用

```typescript
import express from 'express';
import OpenAI from 'openai';

interface ChatMessage {
  role: 'user' | 'assistant' | 'system';
  content: string;
}

class ChatBotWithMemory {
  private memory: ConversationMemory;
  private openai: OpenAI;
  private conversationHistory: ChatMessage[] = [];

  constructor(userId: string, openaiApiKey: string) {
    this.memory = new ConversationMemory(userId);
    this.openai = new OpenAI({ apiKey: openaiApiKey });
  }

  async chat(userMessage: string): Promise<string> {
    // 1. 检索长期记忆
    const longTermMemories = await this.memory.retrieve(userMessage, { topK: 3 });

    // 2. 构建系统提示词
    const systemPrompt = this.buildSystemPrompt(longTermMemories);

    // 3. 调用 OpenAI
    const messages: ChatMessage[] = [
      { role: 'system', content: systemPrompt },
      ...this.conversationHistory,
      { role: 'user', content: userMessage }
    ];

    const completion = await this.openai.chat.completions.create({
      model: 'gpt-4',
      messages: messages as any
    });

    const assistantMessage = completion.choices[0].message.content!;

    // 4. 更新短期记忆
    this.conversationHistory.push(
      { role: 'user', content: userMessage },
      { role: 'assistant', content: assistantMessage }
    );

    // 保留最近 10 轮
    if (this.conversationHistory.length > 20) {
      this.conversationHistory = this.conversationHistory.slice(-20);
    }

    // 5. 保存到长期记忆
    const importance = this.calculateImportance(userMessage);
    await this.memory.save(userMessage, assistantMessage, { 
      importanceScore: importance 
    });

    return assistantMessage;
  }

  private buildSystemPrompt(memories: Memory[]): string {
    if (memories.length === 0) {
      return '你是一个智能助手，请友好地回答用户问题。';
    }

    const memoryContext = memories
      .map(m => `- [${m.memoryType}] ${m.summary}`)
      .join('\n');

    return `你是一个智能助手。以下是用户的历史记忆：\n\n${memoryContext}\n\n请根据这些记忆个性化回复。`;
  }

  private calculateImportance(message: string): number {
    return message.length > 50 || message.includes('重要') ? 0.8 : 0.5;
  }
}

// Express 路由
const app = express();
app.use(express.json());

app.post('/chat', async (req, res) => {
  const { userId, message } = req.body;
  const bot = new ChatBotWithMemory(userId, process.env.OPENAI_API_KEY!);
  
  try {
    const response = await bot.chat(message);
    res.json({ response });
  } catch (error) {
    res.status(500).json({ error: 'Chat failed' });
  }
});

app.listen(3000, () => console.log('Server running on port 3000'));
```

---

## Java 示例

```java
import com.fasterxml.jackson.databind.ObjectMapper;
import okhttp3.*;

import java.io.IOException;
import java.util.List;
import java.util.Map;

public class ConversationMemory {
    private final String userId;
    private final String baseUrl;
    private final OkHttpClient client;
    private final ObjectMapper mapper;

    public ConversationMemory(String userId, String baseUrl) {
        this.userId = userId;
        this.baseUrl = baseUrl;
        this.client = new OkHttpClient();
        this.mapper = new ObjectMapper();
    }

    public String save(String userMessage, String assistantMessage, 
                      String memoryType, double importance) throws IOException {
        Map<String, Object> payload = Map.of(
            "userId", userId,
            "userMessage", userMessage,
            "assistantMessage", assistantMessage,
            "memoryType", memoryType,
            "importanceScore", importance
        );

        RequestBody body = RequestBody.create(
            mapper.writeValueAsString(payload),
            MediaType.parse("application/json")
        );

        Request request = new Request.Builder()
            .url(baseUrl + "/api/memory/save")
            .post(body)
            .build();

        try (Response response = client.newCall(request).execute()) {
            Map<String, String> result = mapper.readValue(
                response.body().string(), 
                Map.class
            );
            return result.get("memoryId");
        }
    }

    public List<Map<String, Object>> retrieve(String query, int topK) 
            throws IOException {
        Map<String, Object> payload = Map.of(
            "userId", userId,
            "query", query,
            "topK", topK
        );

        RequestBody body = RequestBody.create(
            mapper.writeValueAsString(payload),
            MediaType.parse("application/json")
        );

        Request request = new Request.Builder()
            .url(baseUrl + "/api/memory/retrieve")
            .post(body)
            .build();

        try (Response response = client.newCall(request).execute()) {
            Map<String, Object> result = mapper.readValue(
                response.body().string(), 
                Map.class
            );
            return (List<Map<String, Object>>) result.get("memories");
        }
    }
}

// 使用示例
public class Main {
    public static void main(String[] args) throws IOException {
        ConversationMemory memory = new ConversationMemory(
            "user_123", 
            "http://localhost:5000"
        );

        // 保存记忆
        String memoryId = memory.save(
            "我是一名 Java 开发者",
            "明白了，您是 Java 开发者。",
            "fact",
            0.9
        );

        // 检索记忆
        List<Map<String, Object>> memories = memory.retrieve(
            "推荐一个 Web 框架", 
            5
        );

        for (Map<String, Object> m : memories) {
            System.out.printf("[%s] %s (%.2f)%n",
                m.get("memoryType"),
                m.get("summary"),
                m.get("similarityScore")
            );
        }
    }
}
```

---

## C# 示例

```csharp
using System.Net.Http.Json;
using System.Text.Json;

public class ConversationMemory
{
    private readonly string _userId;
    private readonly HttpClient _client;

    public ConversationMemory(string userId, string baseUrl = "http://localhost:5000")
    {
        _userId = userId;
        _client = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public async Task<string> SaveAsync(
        string userMessage,
        string assistantMessage,
        string memoryType = "context",
        double importance = 0.5)
    {
        var payload = new
        {
            userId = _userId,
            userMessage,
            assistantMessage,
            memoryType,
            importanceScore = importance
        };

        var response = await _client.PostAsJsonAsync("/api/memory/save", payload);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SaveResponse>();
        return result.MemoryId;
    }

    public async Task<List<Memory>> RetrieveAsync(
        string query,
        int topK = 5,
        double minScore = 0.6)
    {
        var payload = new
        {
            userId = _userId,
            query,
            topK,
            minScore
        };

        var response = await _client.PostAsJsonAsync("/api/memory/retrieve", payload);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RetrieveResponse>();
        return result.Memories;
    }

    private class SaveResponse
    {
        public string MemoryId { get; set; }
    }

    private class RetrieveResponse
    {
        public List<Memory> Memories { get; set; }
    }
}

public class Memory
{
    public string Id { get; set; }
    public string Summary { get; set; }
    public string FullContent { get; set; }
    public double SimilarityScore { get; set; }
    public double ImportanceScore { get; set; }
}

// 使用示例
var memory = new ConversationMemory("user_123");

// 保存记忆
var memoryId = await memory.SaveAsync(
    "我是一名 C# 开发者",
    "明白了，您是 C# 开发者。",
    "fact",
    0.9
);

// 检索记忆
var memories = await memory.RetrieveAsync("推荐一个 Web 框架", topK: 5);
foreach (var m in memories)
{
    Console.WriteLine($"[{m.MemoryType}] {m.Summary} ({m.SimilarityScore:F2})");
}
```

---

## 测试脚本

### cURL 测试

```bash
#!/bin/bash

# 保存记忆
curl -X POST http://localhost:5000/api/memory/save \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "test_user",
    "userMessage": "我喜欢 Python",
    "assistantMessage": "好的，记住了",
    "memoryType": "preference",
    "importanceScore": 0.9
  }'

# 检索记忆
curl -X POST http://localhost:5000/api/memory/retrieve \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "test_user",
    "query": "推荐一个编程语言",
    "topK": 5
  }'

# 获取最近记忆
curl http://localhost:5000/api/memory/test_user/recent?count=10

# 清理记忆
curl -X POST http://localhost:5000/api/memory/test_user/cleanup?keepTopN=100

# 删除所有记忆
curl -X DELETE http://localhost:5000/api/memory/test_user
```

---

## 前端集成（React）

```typescript
import { useState, useEffect } from 'react';

interface Message {
  role: 'user' | 'assistant';
  content: string;
}

const ChatWithMemory: React.FC<{ userId: string }> = ({ userId }) => {
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState('');
  const [memories, setMemories] = useState([]);

  const sendMessage = async () => {
    // 1. 添加用户消息
    setMessages(prev => [...prev, { role: 'user', content: input }]);

    // 2. 检索记忆
    const memoryResp = await fetch('http://localhost:5000/api/memory/retrieve', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userId, query: input, topK: 3 })
    });
    const { memories: retrievedMemories } = await memoryResp.json();
    setMemories(retrievedMemories);

    // 3. 调用后端 Chat API（带记忆）
    const chatResp = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        userId,
        message: input,
        memories: retrievedMemories
      })
    });
    const { response } = await chatResp.json();

    // 4. 添加助手消息
    setMessages(prev => [...prev, { role: 'assistant', content: response }]);

    // 5. 保存记忆
    await fetch('http://localhost:5000/api/memory/save', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        userId,
        userMessage: input,
        assistantMessage: response
      })
    });

    setInput('');
  };

  return (
    <div>
      <div className="chat-messages">
        {messages.map((msg, i) => (
          <div key={i} className={`message ${msg.role}`}>
            {msg.content}
          </div>
        ))}
      </div>
      
      <div className="chat-input">
        <input
          value={input}
          onChange={e => setInput(e.target.value)}
          onKeyPress={e => e.key === 'Enter' && sendMessage()}
        />
        <button onClick={sendMessage}>发送</button>
      </div>

      <div className="memories-panel">
        <h3>相关记忆</h3>
        {memories.map((m: any) => (
          <div key={m.id}>{m.summary}</div>
        ))}
      </div>
    </div>
  );
};
```

---

## 总结

所有示例都遵循相同的核心流程：

1. **检索记忆** → 调用 `/api/memory/retrieve`
2. **构建上下文** → 短期记忆 + 长期记忆 + 知识库
3. **调用 LLM** → 使用完整上下文生成回复
4. **保存记忆** → 调用 `/api/memory/save`

选择适合你的编程语言，开始集成吧！🚀

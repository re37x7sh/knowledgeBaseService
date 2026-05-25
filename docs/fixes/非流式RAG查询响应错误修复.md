# 非流式 RAG 查询响应错误修复

## 问题描述

**错误信息**：
```
Error: Cannot read properties of undefined (reading 'answer')
```

**触发场景**：
- 前端执行非流式 RAG 查询（`useStream: false`）
- 尝试访问 `response.data.answer` 时报错

---

## 根本原因

### API 响应拦截器设计

`client.ts` 中的响应拦截器已经返回了 `response.data`：

```typescript
// 响应拦截器
apiClient.interceptors.response.use(
  (response) => {
    return response.data  // ← 直接返回 data
  },
  ...
)
```

### 错误的访问方式

`chat.ts` 中访问了两层 `.data`：

```typescript
const response = await ragApi.query(...)

// ❌ 错误：response 已经是 data，再访问 .data.answer 会是 undefined
addMessage('assistant', response.data.answer, response.data.sources)
```

### 实际响应结构

```typescript
// apiClient.post() 返回的是 Promise<T>，不是 Promise<AxiosResponse<T>>
// 因为拦截器已经返回了 response.data

// 正确的结构：
response = {
  question: "...",
  answer: "...",      // ← 直接在 response 上
  sources: [...],     // ← 直接在 response 上
  processingTimeMs: 123,
  tokensUsed: 456
}

// 错误的假设：
response = {
  data: {            // ← 这一层不存在！
    answer: "...",
    sources: [...]
  }
}
```

---

## 解决方案

### 1. 修复 chat.ts 中的访问路径

**修改前**：
```typescript
addMessage('assistant', response.data.answer, response.data.sources)
```

**修改后**：
```typescript
// apiClient 拦截器已返回 response.data，所以直接访问 response.answer
addMessage('assistant', response.answer, response.sources)
```

### 2. 修复 TypeScript 类型定义

**问题**：`apiClient.post<T>()` 默认返回 `Promise<AxiosResponse<T>>`，但实际返回 `Promise<T>`

**解决**：定义自定义 Axios 实例类型

**修改 client.ts**：

```typescript
// 创建自定义 Axios 实例类型，响应直接返回 data
interface CustomAxiosInstance extends Omit<AxiosInstance, 'get' | 'post' | 'put' | 'delete' | 'patch'> {
  get<T = any>(url: string, config?: any): Promise<T>
  post<T = any>(url: string, data?: any, config?: any): Promise<T>
  put<T = any>(url: string, data?: any, config?: any): Promise<T>
  delete<T = any>(url: string, config?: any): Promise<T>
  patch<T = any>(url: string, data?: any, config?: any): Promise<T>
}

const instance = axios.create({...})

// 响应拦截器 - 直接返回 data
instance.interceptors.response.use(
  (response: AxiosResponse) => {
    return response.data  // Promise<T> 而非 Promise<AxiosResponse<T>>
  },
  ...
)

// 导出为自定义类型
const apiClient = instance as CustomAxiosInstance

export default apiClient
```

---

## 修复效果

### 修复前
```typescript
// ❌ 类型错误：response.data 不存在
const response = await ragApi.query(...)
addMessage('assistant', response.data.answer, response.data.sources)
// 运行时错误：Cannot read properties of undefined (reading 'answer')
```

### 修复后
```typescript
// ✅ 类型正确：response 直接包含 answer 和 sources
const response = await ragApi.query(...)
addMessage('assistant', response.answer, response.sources)
// 运行时正常：正确访问到数据
```

---

## 影响范围

### 受益的 API 调用
- ✅ `ragApi.query()` - RAG 非流式查询
- ✅ `documentApi.getList()` - 获取文档列表
- ✅ `documentApi.getById()` - 获取文档详情
- ✅ 所有使用 `apiClient` 的接口调用

### 类型安全改进
- ✅ TypeScript 编译时会检查正确的访问路径
- ✅ 避免运行时 `undefined` 错误
- ✅ IDE 智能提示正确

---

## 验证测试

### 测试步骤

1. **非流式 RAG 查询**
   ```typescript
   const response = await ragApi.query({
     question: "测试问题",
     topK: 5,
     useStream: false
   })
   
   console.log(response.answer)     // ✅ 正常访问
   console.log(response.sources)    // ✅ 正常访问
   ```

2. **检查浏览器 Console**
   - ❌ 修复前：`Cannot read properties of undefined (reading 'answer')`
   - ✅ 修复后：正常显示答案和来源文档

3. **检查 TypeScript 编译**
   - ❌ 修复前：类型 `AxiosResponse<RAGQueryResponse>` 上不存在属性 `answer`
   - ✅ 修复后：编译通过，无类型错误

---

## 关键要点

1. ✅ **响应拦截器已返回 data**：`apiClient` 拦截器直接返回 `response.data`
2. ✅ **无需二次访问**：不应该再访问 `response.data.xxx`，直接访问 `response.xxx`
3. ✅ **类型定义一致**：自定义 `CustomAxiosInstance` 类型匹配实际返回值
4. ✅ **全局影响**：修复后所有 API 调用都受益

---

## 相关问题

这个问题与之前修复的 `VersionManager.vue` 问题类似：
- Phase 20：修复了 `response.data.data` 访问错误
- 当前：修复了 `response.data.answer` 访问错误
- 根本原因相同：响应拦截器已返回 `response.data`

---

**修复时间**: 2025-01-15  
**影响文件**: `client.ts`, `chat.ts`  
**测试状态**: ✅ 编译通过

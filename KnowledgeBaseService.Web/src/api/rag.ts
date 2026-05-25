import apiClient from './client'
import type { RAGQueryResponse, RAGQueryRequest } from '@/types/rag'

/**
 * RAG 查询 API
 */
export const ragApi = {
  /**
   * 执行 RAG 查询
   */
  query(request: RAGQueryRequest) {
    return apiClient.post<RAGQueryResponse>('/rag/query', request)
  },

  /**
   * 流式 RAG 查询
   */
  async *queryStream(request: RAGQueryRequest) {
    const response = await fetch('/api/rag/query-stream', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    })

    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`)
    }

    const reader = response.body?.getReader()
    if (!reader) throw new Error('No response body')

    const decoder = new TextDecoder()
    let buffer = ''

    try {
      while (true) {
        const { done, value } = await reader.read()
        if (done) break

        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')

        for (let i = 0; i < lines.length - 1; i++) {
          const line = lines[i].trim()
          if (line.startsWith('data: ')) {
            try {
              const jsonStr = line.slice(6)
              const data = JSON.parse(jsonStr)
              
              if (data.type === 'done') {
                return
              }
              
              yield {
                type: data.type,
                content: data.data
              }
            } catch (e) {
              console.error('Failed to parse SSE data:', e, 'line:', line)
            }
          }
        }

        buffer = lines[lines.length - 1]
      }
    } finally {
      reader.releaseLock()
    }
  },

  /**
   * 按类别查询
   */
  queryByCategory(category: string, question: string) {
    return apiClient.post<RAGQueryResponse>('/rag/query-by-category', {
      category,
      question,
    })
  },
}

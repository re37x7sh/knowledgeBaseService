import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { ChatMessage, SourceDocument } from '@/types/rag'
import { ragApi } from '@/api/rag'

export const useChatStore = defineStore('chat', () => {
  const messages = ref<ChatMessage[]>([])
  const loading = ref(false)
  const streaming = ref(false)

  /**
   * 添加消息
   */
  const addMessage = (role: 'user' | 'assistant', content: string, sources?: SourceDocument[]) => {
    messages.value.push({
      id: `${Date.now()}-${Math.random()}`,
      role,
      content,
      sources,
      timestamp: new Date().toISOString(),
    })
  }

  /**
   * 执行 RAG 查询
   */
  const query = async (question: string, documentIds?: string[], enableHybridMode?: boolean) => {
    if (loading.value || streaming.value) return

    addMessage('user', question)
    loading.value = true

    try {
      const response = await ragApi.query({
        question,
        topK: 5,
        useStream: false,
        documentIds,
        enableHybridMode,
      })

      // apiClient 拦截器已返回 response.data，所以直接访问 response.answer
      addMessage('assistant', response.answer, response.sources)
      return response
    } catch (error) {
      const errorMsg = error instanceof Error ? error.message : 'Query failed'
      addMessage('assistant', `Error: ${errorMsg}`)
      throw error
    } finally {
      loading.value = false
    }
  }

  /**
   * 执行流式 RAG 查询
   */
  const queryStream = async (question: string, documentIds?: string[], enableHybridMode?: boolean) => {
    if (loading.value || streaming.value) return

    addMessage('user', question)
    // 先添加一个空的助手消息，用于实时更新
    const assistantMessageId = `${Date.now()}-${Math.random()}`
    messages.value.push({
      id: assistantMessageId,
      role: 'assistant',
      content: '',
      timestamp: new Date().toISOString(),
    })
    
    streaming.value = true
    loading.value = true

    try {
      let fullAnswer = ''
      let sources: any[] = []

      console.log('🚀 [RAG Stream] 开始流式查询...', { question, documentIds, enableHybridMode })
      
      for await (const chunk of ragApi.queryStream({
        question,
        topK: 5,
        useStream: true,
        documentIds,
        enableHybridMode,
      })) {
        console.log('📦 [RAG Stream] 收到数据块:', chunk)
        
        if (chunk.type === 'sources') {
          console.log('📚 [RAG Stream] 收到 sources 类型消息')
          // 解析 sources JSON
          try {
            console.log('🔍 [RAG Stream] sources 原始内容:', chunk.content)
            const sourcesData = JSON.parse(chunk.content)
            sources = sourcesData.sources || []
            console.log('✅ [RAG Stream] 解析成功，sources 数量:', sources.length)
            console.log('📄 [RAG Stream] sources 详情:', sources)
            
            // 更新消息的 sources
            const lastMessage = messages.value[messages.value.length - 1]
            if (lastMessage && lastMessage.role === 'assistant') {
              lastMessage.sources = sources
              console.log('💾 [RAG Stream] 已更新消息对象的 sources 属性')
              console.log('🔗 [RAG Stream] 当前消息对象:', lastMessage)
            } else {
              console.warn('⚠️ [RAG Stream] 未找到最后一条助手消息')
            }
          } catch (e) {
            console.error('❌ [RAG Stream] 解析 sources 失败:', e)
            console.error('❌ [RAG Stream] 原始数据:', chunk.content)
          }
        } else if (chunk.type === 'content') {
          fullAnswer += chunk.content
          // 实时更新消息内容（逐字显示）
          const lastMessage = messages.value[messages.value.length - 1]
          if (lastMessage && lastMessage.role === 'assistant') {
            lastMessage.content = fullAnswer
          }
        } else if (chunk.type === 'done') {
          console.log('✅ [RAG Stream] 流式查询完成')
        } else {
          console.warn('⚠️ [RAG Stream] 未知消息类型:', chunk.type)
        }
      }
      
      console.log('🏁 [RAG Stream] 查询结束，最终 sources 数量:', sources.length)
    } catch (error) {
      const errorMsg = error instanceof Error ? error.message : 'Query failed'
      const lastMessage = messages.value[messages.value.length - 1]
      if (lastMessage && lastMessage.role === 'assistant') {
        lastMessage.content = `Error: ${errorMsg}`
      }
      throw error
    } finally {
      streaming.value = false
      loading.value = false
    }
  }

  /**
   * 清除对话历史
   */
  const clearMessages = () => {
    messages.value = []
  }

  /**
   * 删除某条消息
   */
  const deleteMessage = (id: string) => {
    messages.value = messages.value.filter((msg) => msg.id !== id)
  }

  return {
    messages,
    loading,
    streaming,
    addMessage,
    query,
    queryStream,
    clearMessages,
    deleteMessage,
  }
})

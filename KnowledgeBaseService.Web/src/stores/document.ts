import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type {
  DocumentResponse,
  ImportProgress,
  UpsertDocumentContentRequest,
  UpsertDocumentContentResponse,
} from '@/types/document'
import { documentApi } from '@/api/document'

export const useDocumentStore = defineStore('document', () => {
  const documents = ref<DocumentResponse[]>([])
  const importProgress = ref<Map<string, ImportProgress>>(new Map())
  const loading = ref(false)
  const total = ref(0)

  const sortedDocuments = computed(() =>
    [...documents.value].sort((a, b) => 
      new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
    )
  )

  /**
   * 加载文档列表
   */
  const fetchDocuments = async (skip: number = 0, take: number = 10) => {
    loading.value = true
    try {
      const response = await documentApi.listDocuments(skip, take)
      documents.value = response.items
      total.value = response.total
    } catch (error) {
      console.error('Failed to fetch documents:', error)
      throw error
    } finally {
      loading.value = false
    }
  }

  /**
   * 导入单个文件
   */
  const importFile = async (file: File, title?: string, category?: string) => {
    const progressKey = `${Date.now()}-${file.name}`
    
    importProgress.value.set(progressKey, {
      documentId: '',
      fileName: file.name,
      status: 'uploading',
      progress: 0,
      createdAt: new Date().toISOString(),
    })

    try {
      // 模拟上传进度
      const progressInterval = setInterval(() => {
        const current = importProgress.value.get(progressKey)
        if (current && current.progress < 90) {
          current.progress += Math.random() * 30
        }
      }, 500)

      const response = await documentApi.importFile(file, title, category)
      clearInterval(progressInterval)

      const progress = importProgress.value.get(progressKey)!
      progress.progress = 100
      progress.status = 'indexing'
      progress.documentId = response.id

      // 模拟索引进度
      await simulateIndexing(progressKey)

      progress.status = 'completed'
      documents.value.unshift(response)

      return response
    } catch (error) {
      const progress = importProgress.value.get(progressKey)
      if (progress) {
        progress.status = 'failed'
        progress.error = error instanceof Error ? error.message : 'Unknown error'
      }
      throw error
    }
  }

  /**
   * 批量导入文件
   */
  const importFilesBatch = async (files: File[], titles?: string[], categories?: string[]) => {
    const progressKeys = files.map((file, index) => {
      const key = `batch-${Date.now()}-${index}`
      importProgress.value.set(key, {
        documentId: '',
        fileName: file.name,
        status: 'uploading',
        progress: 0,
        createdAt: new Date().toISOString(),
      })
      return key
    })

    try {
      const progressInterval = setInterval(() => {
        progressKeys.forEach((key) => {
          const current = importProgress.value.get(key)
          if (current && current.progress < 90 && current.status === 'uploading') {
            current.progress += Math.random() * 20
          }
        })
      }, 300)

      const response = await documentApi.importFilesBatch(files, titles, categories)
      clearInterval(progressInterval)

      // 更新每个文件的进度
      for (let i = 0; i < progressKeys.length; i++) {
        const key = progressKeys[i]
        const result = response.results[i]
        const progress = importProgress.value.get(key)!

        if (result.success) {
          progress.progress = 100
          progress.status = 'indexing'
          progress.documentId = result.documentId || ''
          await simulateIndexing(key)
          progress.status = 'completed'
        } else {
          progress.status = 'failed'
          progress.error = result.error
        }
      }

      await fetchDocuments(0, 10)
      return response
    } catch (error) {
      progressKeys.forEach((key) => {
        const progress = importProgress.value.get(key)
        if (progress) {
          progress.status = 'failed'
          progress.error = error instanceof Error ? error.message : 'Unknown error'
        }
      })
      throw error
    }
  }

  /**
   * 模拟索引进度
   */
  const simulateIndexing = (key: string) => {
    return new Promise<void>((resolve) => {
      const progress = importProgress.value.get(key)
      if (!progress) return

      let indexProgress = 0
      const interval = setInterval(() => {
        indexProgress += Math.random() * 50
        if (indexProgress >= 100) {
          clearInterval(interval)
          resolve()
        }
      }, 800)
    })
  }

  /**
   * 删除文档
   */
  const deleteDocument = async (id: string) => {
    try {
      await documentApi.deleteDocument(id)
      documents.value = documents.value.filter((doc) => doc.id !== id)
      total.value--
    } catch (error) {
      console.error('Failed to delete document:', error)
      throw error
    }
  }

  /**
   * 外部接口：增量同步文档内容
   */
  const syncDocumentContent = async (
    payload: UpsertDocumentContentRequest
  ): Promise<UpsertDocumentContentResponse> => {
    try {
      const response = await documentApi.syncContent(payload)
      // 同步完成后刷新列表，确保新文档可以看到
      const take = documents.value.length > 0 ? documents.value.length : 10
      await fetchDocuments(0, take)
      return response
    } catch (error) {
      console.error('Failed to sync document content:', error)
      throw error
    }
  }

  /**
   * 清除导入进度记录
   */
  const clearProgress = (key: string) => {
    importProgress.value.delete(key)
  }

  /**
   * 获取所有导入进度
   */
  const getAllProgress = () => {
    return Array.from(importProgress.value.values())
  }

  return {
    documents,
    importProgress,
    loading,
    total,
    sortedDocuments,
    fetchDocuments,
    importFile,
    importFilesBatch,
    deleteDocument,
    syncDocumentContent,
    clearProgress,
    getAllProgress,
  }
})

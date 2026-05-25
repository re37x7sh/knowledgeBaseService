import apiClient from './client'
import type {
  DocumentResponse,
  ListResponse,
  SupportedFormat,
  UpsertDocumentContentRequest,
  UpsertDocumentContentResponse,
} from '@/types/document'

/**
 * 文档管理 API
 */
export const documentApi = {
  /**
   * 获取支持的文件格式
   */
  getSupportedFormats() {
    return apiClient.get<SupportedFormat[]>('/documents/supported-formats')
  },

  /**
   * 单文件导入
   */
  importFile(file: File, title?: string, category?: string) {
    const formData = new FormData()
    formData.append('file', file)
    if (title) formData.append('title', title)
    if (category) formData.append('category', category)

    return apiClient.post<DocumentResponse>('/documents/import-from-file', formData, {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    })
  },

  /**
   * 批量导入文件
   */
  importFilesBatch(files: File[], titles?: string[], categories?: string[]) {
    const formData = new FormData()
    files.forEach((file, index) => {
      formData.append(`files`, file)
      if (titles?.[index]) formData.append(`titles[${index}]`, titles[index])
      if (categories?.[index]) formData.append(`categories[${index}]`, categories[index])
    })

    return apiClient.post<{
      successCount: number
      failedCount: number
      results: Array<{ fileName: string; success: boolean; error?: string; documentId?: string }>
    }>('/documents/import-files-batch', formData, {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    })
  },

  /**
   * 获取文档列表
   */
  listDocuments(skip: number = 0, take: number = 10) {
    return apiClient.get<ListResponse>('/documents/list', {
      params: { skip, take },
    })
  },

  /**
   * 获取单个文档
   */
  getDocument(id: string) {
    return apiClient.get<DocumentResponse>(`/documents/${id}`)
  },

  /**
   * 删除文档
   */
  deleteDocument(id: string) {
    return apiClient.delete(`/documents/${id}`)
  },

  /**
   * 外部接口：增量同步文档内容
   */
  syncContent(payload: UpsertDocumentContentRequest) {
    return apiClient.post<UpsertDocumentContentResponse>('/documents/sync-content', payload)
  },
}

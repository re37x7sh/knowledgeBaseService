/**
 * 文档相关类型定义
 */

export interface Document {
  id: string
  title: string
  content: string
  category: string
  sourceUrl?: string
  createdAt: string
  updatedAt: string
}

export interface DocumentResponse {
  id: string
  title: string
  category: string
  sourceUrl?: string
  fileExtension?: string
  createdAt: string
  updatedAt: string
  content?: string
}

export interface CreateDocumentRequest {
  title: string
  content: string
  category: string
  sourceUrl?: string
}

export interface ImportProgress {
  documentId: string
  fileName: string
  status: 'pending' | 'uploading' | 'indexing' | 'completed' | 'failed'
  progress: number // 0-100
  error?: string
  createdAt: string
}

export interface SupportedFormat {
  extension: string
  mimeType: string
  description: string
  maxSize: string
  example?: string
}

export interface ListResponse {
  items: DocumentResponse[]
  total: number
  skip: number
  take: number
}

export interface UpsertDocumentContentRequest {
  documentId?: string
  name?: string
  content: string
  category?: string
  sourceUrl?: string
  appendDelimiter?: string
  changeLog?: string
  updatedBy?: string
  tag?: string
  fileExtension?: string
}

export interface UpsertDocumentContentResponse {
  documentId: string
  name: string
  created: boolean
  version: number
  contentLength: number
  category: string
  updatedAt: string
  message: string
}

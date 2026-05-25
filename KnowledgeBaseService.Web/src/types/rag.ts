/**
 * RAG 查询相关类型定义
 */

export interface RAGQueryRequest {
  question: string
  topK?: number
  useStream?: boolean
  documentIds?: string[]  // 可选的文档过滤：空/未定义时搜索全库，指定时仅搜索这些文档
  enableHybridMode?: boolean  // 启用混合模式：优先知识库，不足时补充通用知识
}

export interface RAGQueryResponse {
  answer: string
  sources: SourceDocument[]
  tokensUsed?: {
    embedding: number
    completion: number
  }
  responseTime?: number
}

export interface SourceDocument {
  documentId: string
  title: string
  score: number
  snippet: string
  sourceUrl?: string
  fileType?: string  // 文件类型：image/pdf/docx等
  imageBase64?: string  // 图片的Base64编码（仅当fileType为image时有值）
  matchHint?: string  // 命中文本的上下文提示（用于图片定位）
}

export interface ChatMessage {
  id: string
  role: 'user' | 'assistant'
  content: string
  sources?: SourceDocument[]
  timestamp: string
}

export interface ChatHistory {
  messages: ChatMessage[]
  documentId?: string
}

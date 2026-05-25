/**
 * 版本管理相关类型定义
 */

export interface VersionResponse {
  id: string
  documentId: string
  versionNumber: number
  title: string
  tag?: string
  changeLog?: string
  changeSummary?: string
  category?: string
  createdBy?: string
  createdAt: string
  isCurrent: boolean
  contentSize: number
}

export interface VersionContentResponse {
  id: string
  versionNumber: number
  title: string
  content: string
  createdBy?: string
  createdAt: string
  changeLog?: string
}

export interface CompareVersionResponse {
  fromVersionNumber: number
  toVersionNumber: number
  diff?: string
  linesAdded: number
  linesRemoved: number
  linesModified: number
}

export interface VersionStatisticsResponse {
  documentId: string
  totalVersions: number
  firstVersionDate?: string
  lastVersionDate?: string
  averageSize: number
  maxSize: number
  minSize: number
  totalSize: number
  taggedVersions: number
  mostFrequentEditor?: string
  tags: string[]
}

export interface CreateVersionRequest {
  documentId: string
  content: string
  title: string
  changeLog?: string
  createdBy?: string
  tag?: string
  category?: string
}

export interface VersionTag {
  name: string
  count: number
}

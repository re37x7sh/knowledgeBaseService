/**
 * 文档版本管理 API 调用
 */

import apiClient from './client'
import type {
  VersionResponse,
  VersionContentResponse,
  CompareVersionResponse,
  VersionStatisticsResponse,
  CreateVersionRequest,
} from '@/types/version'

export const versionApi = {
  /**
   * 获取文档的所有版本
   */
  getVersions(documentId: string, skip = 0, take = 20) {
    return apiClient.get<VersionResponse[]>(
      `/documentversions/document/${documentId}`,
      { params: { skip, take } }
    )
  },

  /**
   * 获取版本内容
   */
  getVersionContent(versionId: string) {
    return apiClient.get<VersionContentResponse>(
      `/documentversions/${versionId}/content`
    )
  },

  /**
   * 创建新版本
   */
  createVersion(data: CreateVersionRequest) {
    return apiClient.post<VersionResponse>('/documentversions/create', data)
  },

  /**
   * 比较两个版本
   */
  compareVersions(documentId: string, fromVersion: number, toVersion: number) {
    return apiClient.get<CompareVersionResponse>(
      `/documentversions/document/${documentId}/compare`,
      { params: { fromVersion, toVersion } }
    )
  },

  /**
   * 回滚到指定版本
   */
  rollbackToVersion(documentId: string, targetVersion: number, reason?: string) {
    return apiClient.post(
      `/documentversions/document/${documentId}/rollback`,
      {},
      { params: { targetVersion, reason } }
    )
  },

  /**
   * 添加版本标签
   */
  addTag(versionId: string, tag: string) {
    return apiClient.post(
      `/documentversions/${versionId}/tag`,
      {},
      { params: { tag } }
    )
  },

  /**
   * 删除版本
   */
  deleteVersion(versionId: string) {
    return apiClient.delete(`/documentversions/${versionId}`)
  },

  /**
   * 获取版本统计信息
   */
  getStatistics(documentId: string) {
    return apiClient.get<VersionStatisticsResponse>(
      `/documentversions/document/${documentId}/statistics`
    )
  },

  /**
   * 获取当前活跃版本
   */
  getCurrentVersion(documentId: string) {
    return apiClient.get<VersionContentResponse>(
      `/documentversions/document/${documentId}/current`
    )
  },

  /**
   * 导出版本
   */
  exportVersion(versionId: string, format: 'markdown' | 'text' | 'html' = 'markdown') {
    return apiClient.get(`/documentversions/${versionId}/export`, {
      params: { format },
      responseType: 'blob',
    })
  },
}

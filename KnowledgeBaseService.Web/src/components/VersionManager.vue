<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { versionApi } from '@/api/version'
import type { VersionResponse, VersionContentResponse, CompareVersionResponse } from '@/types/version'

interface Props {
  documentId: string
  documentTitle: string
}

const props = defineProps<Props>()
const emit = defineEmits<{
  back: []
}>()

// 标签页状态
type TabType = 'list' | 'compare' | 'statistics'
const activeTab = ref<TabType>('list')

// 版本列表
const versions = ref<VersionResponse[]>([])
const loading = ref(false)
const selectedVersion = ref<VersionResponse | null>(null)
const versionContent = ref<VersionContentResponse | null>(null)
const showContentModal = ref(false)

// 分页
const skip = ref(0)
const take = ref(20)
const totalVersions = ref(0)

// 比较
const compareFrom = ref<number>(1)
const compareTo = ref<number>(2)
const compareResult = ref<CompareVersionResponse | null>(null)
const comparing = ref(false)

// 统计信息
const statistics = ref<any>(null)
const statsLoading = ref(false)

// 回滚
const showRollbackModal = ref(false)
const rollbackTarget = ref<number>(1)
const rollbackReason = ref('')
const rollingBack = ref(false)

// 标签编辑
const showTagModal = ref(false)
const editingVersion = ref<VersionResponse | null>(null)
const newTag = ref('')

// 创建新版本
const showCreateModal = ref(false)
const creatingVersion = ref(false)
const contentMode = ref<'edit' | 'upload'>('edit') // 编辑模式或上传模式
const newVersionData = ref({
  title: '',
  content: '',
  changeLog: '',
  tag: '',
  createdBy: 'user'
})
const fileInput = ref<HTMLInputElement | null>(null)
const uploadedFile = ref<File | null>(null)

// 获取版本列表
const loadVersions = async () => {
  loading.value = true
  try {
    const response = await versionApi.getVersions(props.documentId, skip.value, take.value)
    versions.value = response
    totalVersions.value = versions.value.length
  } catch (error) {
    console.error('Failed to load versions:', error)
  } finally {
    loading.value = false
  }
}

// 查看版本内容
const viewVersion = async (version: VersionResponse) => {
  selectedVersion.value = version
  try {
    const response = await versionApi.getVersionContent(version.id)
    versionContent.value = response
    showContentModal.value = true
  } catch (error) {
    console.error('Failed to load version content:', error)
  }
}

// 比较版本
const compareVersions = async () => {
  if (compareFrom.value === compareTo.value) {
    alert('源版本和目标版本不能相同')
    return
  }

  comparing.value = true
  try {
    const response = await versionApi.compareVersions(
      props.documentId,
      compareFrom.value,
      compareTo.value
    )
    compareResult.value = response
  } catch (error) {
    console.error('Failed to compare versions:', error)
    alert('比较失败')
  } finally {
    comparing.value = false
  }
}

// 加载统计信息
const loadStatistics = async () => {
  statsLoading.value = true
  try {
    const response = await versionApi.getStatistics(props.documentId)
    statistics.value = response
  } catch (error) {
    console.error('Failed to load statistics:', error)
  } finally {
    statsLoading.value = false
  }
}

// 回滚版本
const confirmRollback = async () => {
  if (!rollbackTarget.value || rollbackTarget.value < 1) {
    alert('请输入有效的版本号')
    return
  }

  rollingBack.value = true
  try {
    await versionApi.rollbackToVersion(props.documentId, rollbackTarget.value, rollbackReason.value)
    alert('版本回滚成功')
    showRollbackModal.value = false
    await loadVersions()
  } catch (error) {
    console.error('Failed to rollback:', error)
    alert('版本回滚失败')
  } finally {
    rollingBack.value = false
  }
}

// 添加标签
const confirmAddTag = async () => {
  if (!editingVersion.value || !newTag.value.trim()) {
    alert('请输入标签名称')
    return
  }

  try {
    await versionApi.addTag(editingVersion.value.id, newTag.value.trim())
    alert('标签添加成功')
    showTagModal.value = false
    newTag.value = ''
    await loadVersions()
  } catch (error) {
    console.error('Failed to add tag:', error)
    alert('标签添加失败')
  }
}

// 删除版本
const deleteVersion = async (version: VersionResponse) => {
  if (version.isCurrent) {
    alert('不能删除当前活跃版本')
    return
  }

  if (!confirm(`确定要删除版本 ${version.versionNumber} 吗？`)) {
    return
  }

  try {
    await versionApi.deleteVersion(version.id)
    alert('版本删除成功')
    await loadVersions()
  } catch (error) {
    console.error('Failed to delete version:', error)
    alert('版本删除失败')
  }
}

// 导出版本
const exportVersion = async (version: VersionResponse, format: 'markdown' | 'text' | 'html') => {
  try {
    const response = await versionApi.exportVersion(version.id, format)
    const url = window.URL.createObjectURL(new Blob([response]))
    const link = document.createElement('a')
    link.href = url
    link.setAttribute('download', `${props.documentTitle}_v${version.versionNumber}.${getFileExtension(format)}`)
    document.body.appendChild(link)
    link.click()
    link.parentNode?.removeChild(link)
  } catch (error) {
    console.error('Failed to export version:', error)
    alert('导出失败')
  }
}

const getFileExtension = (format: string) => {
  return format === 'html' ? 'html' : format === 'text' ? 'txt' : 'md'
}

// 创建新版本
const createNewVersion = async () => {
  if (!newVersionData.value.title.trim() || !newVersionData.value.content.trim()) {
    alert('请填写标题和内容')
    return
  }

  creatingVersion.value = true
  try {
    await versionApi.createVersion({
      documentId: props.documentId,
      title: newVersionData.value.title,
      content: newVersionData.value.content,
      changeLog: newVersionData.value.changeLog || undefined,
      tag: newVersionData.value.tag || undefined,
      createdBy: newVersionData.value.createdBy || 'user'
    })
    alert('版本创建成功')
    showCreateModal.value = false
    resetCreateForm()
    await loadVersions()
  } catch (error) {
    console.error('Failed to create version:', error)
    alert('版本创建失败')
  } finally {
    creatingVersion.value = false
  }
}

// 打开创建版本对话框
const openCreateModal = async () => {
  // 获取当前版本的内容作为模板
  try {
    const current = await versionApi.getCurrentVersion(props.documentId)
    newVersionData.value.content = current.content
    newVersionData.value.title = current.title || ''
  } catch (error) {
    // 如果获取失败，就用空内容
    console.error('Failed to get current version:', error)
  }
  contentMode.value = 'edit'
  uploadedFile.value = null
  showCreateModal.value = true
}

// 处理文件选择
const handleFileSelect = (event: Event) => {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) return

  uploadedFile.value = file
  // 根据文件类型读取内容
  const reader = new FileReader()
  reader.onload = (e) => {
    const content = e.target?.result as string
    if (content) {
      newVersionData.value.content = content
      // 尝试从文件名推断标题
      if (!newVersionData.value.title) {
        newVersionData.value.title = file.name.replace(/\.[^/.]+$/, '')
      }
    }
  }
  reader.readAsText(file)
}

// 触发文件选择
const triggerFileUpload = () => {
  fileInput.value?.click()
}

// 重置创建表单
const resetCreateForm = () => {
  newVersionData.value = {
    title: '',
    content: '',
    changeLog: '',
    tag: '',
    createdBy: 'user'
  }
  uploadedFile.value = null
}

// 格式化文件大小
const formatSize = (bytes: number) => {
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i]
}

// 格式化日期
const formatDate = (date: string) => {
  return new Date(date).toLocaleString('zh-CN')
}

// 初始化
onMounted(() => {
  loadVersions()
})

// 计算分页信息
const pageInfo = computed(() => {
  const current = Math.floor(skip.value / take.value) + 1
  const total = Math.ceil(totalVersions.value / take.value)
  return { current, total }
})

const nextPage = () => {
  if ((skip.value + take.value) < totalVersions.value) {
    skip.value += take.value
    loadVersions()
  }
}

const prevPage = () => {
  if (skip.value > 0) {
    skip.value -= take.value
    loadVersions()
  }
}

const goBackToDocuments = () => {
  emit('back')
}
</script>

<template>
  <div class="version-manager">
    <div class="header">
      <div class="header-top">
        <el-button link icon="ArrowLeft" @click="goBackToDocuments">
          返回文档列表
        </el-button>
        <h2>📚 版本管理 - {{ documentTitle }}</h2>
      </div>
      <div class="tab-buttons">
        <button
          :class="{ active: activeTab === 'list' }"
          @click="activeTab = 'list'"
          class="tab-btn"
        >
          版本列表
        </button>
        <button
          :class="{ active: activeTab === 'compare' }"
          @click="activeTab = 'compare'; compareResult = null"
          class="tab-btn"
        >
          版本比较
        </button>
        <button
          :class="{ active: activeTab === 'statistics' }"
          @click="activeTab = 'statistics'; loadStatistics()"
          class="tab-btn"
        >
          统计信息
        </button>
      </div>
    </div>

    <!-- 版本列表标签页 -->
    <div v-if="activeTab === 'list'" class="tab-content">
      <div class="action-buttons">
        <button @click="openCreateModal" class="btn btn-success">➕ 创建新版本</button>
        <button @click="showRollbackModal = true" class="btn btn-warning">🔄 回滚版本</button>
        <button @click="loadVersions" class="btn btn-secondary">🔄 刷新</button>
      </div>

      <div v-if="loading" class="loading">加载中...</div>
      <div v-else-if="versions.length === 0" class="empty">暂无版本记录</div>
      <div v-else class="versions-table">
        <table>
          <thead>
            <tr>
              <th>版本号</th>
              <th>标题</th>
              <th>标签</th>
              <th>编辑者</th>
              <th>创建时间</th>
              <th>大小</th>
              <th>状态</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="version in versions" :key="version.id" :class="{ current: version.isCurrent }">
              <td>v{{ version.versionNumber }}</td>
              <td class="title-cell">{{ version.title }}</td>
              <td>
                <span v-if="version.tag" class="badge">{{ version.tag }}</span>
                <span v-else class="badge badge-empty">-</span>
              </td>
              <td>{{ version.createdBy || 'system' }}</td>
              <td>{{ formatDate(version.createdAt) }}</td>
              <td>{{ formatSize(version.contentSize) }}</td>
              <td>
                <span v-if="version.isCurrent" class="badge badge-current">当前版本</span>
              </td>
              <td class="actions">
                <button @click="viewVersion(version)" class="action-btn" title="查看">👁️</button>
                <button @click="() => { editingVersion = version; showTagModal = true }" class="action-btn" title="添加标签">🏷️</button>
                <div class="export-menu">
                  <button class="action-btn" title="导出">💾</button>
                  <div class="export-options">
                    <button @click="exportVersion(version, 'markdown')">Markdown</button>
                    <button @click="exportVersion(version, 'text')">Text</button>
                    <button @click="exportVersion(version, 'html')">HTML</button>
                  </div>
                </div>
                <button
                  v-if="!version.isCurrent"
                  @click="deleteVersion(version)"
                  class="action-btn action-delete"
                  title="删除"
                >
                  🗑️
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- 分页 -->
      <div v-if="versions.length > 0" class="pagination">
        <button @click="prevPage" :disabled="skip === 0" class="btn btn-sm">上一页</button>
        <span class="page-info">第 {{ pageInfo.current }} / {{ pageInfo.total }} 页</span>
        <button @click="nextPage" :disabled="(skip + take) >= totalVersions" class="btn btn-sm">下一页</button>
      </div>
    </div>

    <!-- 版本比较标签页 -->
    <div v-if="activeTab === 'compare'" class="tab-content">
      <div class="compare-section">
        <div class="compare-inputs">
          <div>
            <label>源版本号：</label>
            <input v-model.number="compareFrom" type="number" min="1" />
          </div>
          <div>
            <label>目标版本号：</label>
            <input v-model.number="compareTo" type="number" min="1" />
          </div>
          <button @click="compareVersions" :disabled="comparing" class="btn btn-primary">
            {{ comparing ? '比较中...' : '比较版本' }}
          </button>
        </div>

        <div v-if="compareResult" class="compare-result">
          <div class="stats">
            <div class="stat-item">
              <span class="stat-label">新增行数：</span>
              <span class="stat-value added">+{{ compareResult.linesAdded }}</span>
            </div>
            <div class="stat-item">
              <span class="stat-label">删除行数：</span>
              <span class="stat-value removed">-{{ compareResult.linesRemoved }}</span>
            </div>
            <div class="stat-item">
              <span class="stat-label">修改行数：</span>
              <span class="stat-value modified">~{{ compareResult.linesModified }}</span>
            </div>
          </div>

          <div class="diff-content">
            <h4>差异详情：</h4>
            <pre>{{ compareResult.diff }}</pre>
          </div>
        </div>
      </div>
    </div>

    <!-- 统计信息标签页 -->
    <div v-if="activeTab === 'statistics'" class="tab-content">
      <div v-if="statsLoading" class="loading">加载统计信息中...</div>
      <div v-else-if="statistics" class="statistics-grid">
        <div class="stat-card">
          <div class="stat-title">总版本数</div>
          <div class="stat-value">{{ statistics.totalVersions }}</div>
        </div>
        <div class="stat-card">
          <div class="stat-title">已标记版本</div>
          <div class="stat-value">{{ statistics.taggedVersions }}</div>
        </div>
        <div class="stat-card">
          <div class="stat-title">平均大小</div>
          <div class="stat-value">{{ formatSize(statistics.averageSize) }}</div>
        </div>
        <div class="stat-card">
          <div class="stat-title">最大版本</div>
          <div class="stat-value">{{ formatSize(statistics.maxSize) }}</div>
        </div>
        <div class="stat-card">
          <div class="stat-title">最小版本</div>
          <div class="stat-value">{{ formatSize(statistics.minSize) }}</div>
        </div>
        <div class="stat-card">
          <div class="stat-title">总存储大小</div>
          <div class="stat-value">{{ formatSize(statistics.totalSize) }}</div>
        </div>
        <div class="stat-card" v-if="statistics.mostFrequentEditor">
          <div class="stat-title">最活跃编辑者</div>
          <div class="stat-value">{{ statistics.mostFrequentEditor }}</div>
        </div>
        <div class="stat-card">
          <div class="stat-title">首版本时间</div>
          <div class="stat-value small">{{ statistics.firstVersionDate ? formatDate(statistics.firstVersionDate) : '-' }}</div>
        </div>

        <div class="tags-section" v-if="statistics.tags.length > 0">
          <h4>版本标签：</h4>
          <div class="tags-list">
            <span v-for="tag in statistics.tags" :key="tag" class="badge">{{ tag }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- 版本内容模态框 -->
    <div v-if="showContentModal" class="modal-overlay" @click.self="showContentModal = false">
      <div class="modal">
        <div class="modal-header">
          <h3>{{ versionContent?.title }} - 版本 {{ versionContent?.versionNumber }}</h3>
          <button @click="showContentModal = false" class="close-btn">✕</button>
        </div>
        <div class="modal-content">
          <div class="version-meta">
            <span>📅 {{ versionContent?.createdAt ? formatDate(versionContent.createdAt) : '-' }}</span>
            <span v-if="versionContent?.createdBy">👤 {{ versionContent.createdBy }}</span>
          </div>
          <div v-if="versionContent?.changeLog" class="change-log">
            <strong>变更说明：</strong>
            <p>{{ versionContent.changeLog }}</p>
          </div>
          <pre class="content">{{ versionContent?.content }}</pre>
        </div>
      </div>
    </div>

    <!-- 回滚模态框 -->
    <div v-if="showRollbackModal" class="modal-overlay" @click.self="showRollbackModal = false">
      <div class="modal">
        <div class="modal-header">
          <h3>🔄 回滚版本</h3>
          <button @click="showRollbackModal = false" class="close-btn">✕</button>
        </div>
        <div class="modal-content">
          <div class="form-group">
            <label>目标版本号：</label>
            <input v-model.number="rollbackTarget" type="number" min="1" />
          </div>
          <div class="form-group">
            <label>回滚原因（可选）：</label>
            <textarea v-model="rollbackReason" placeholder="输入回滚原因..."></textarea>
          </div>
          <div class="modal-actions">
            <button @click="showRollbackModal = false" class="btn btn-secondary">取消</button>
            <button @click="confirmRollback" :disabled="rollingBack" class="btn btn-danger">
              {{ rollingBack ? '回滚中...' : '确认回滚' }}
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- 标签编辑模态框 -->
    <div v-if="showTagModal" class="modal-overlay" @click.self="showTagModal = false">
      <div class="modal">
        <div class="modal-header">
          <h3>🏷️ 添加版本标签</h3>
          <button @click="showTagModal = false" class="close-btn">✕</button>
        </div>
        <div class="modal-content">
          <p class="modal-text">为版本 {{ editingVersion?.versionNumber }} 添加标签</p>
          <input
            v-model="newTag"
            type="text"
            placeholder="输入标签名称（例如：v1.0、release、draft）"
            @keyup.enter="confirmAddTag"
          />
          <div class="modal-actions">
            <button @click="showTagModal = false" class="btn btn-secondary">取消</button>
            <button @click="confirmAddTag" class="btn btn-primary">确认</button>
          </div>
        </div>
      </div>
    </div>

    <!-- 创建新版本 Modal -->
    <div v-if="showCreateModal" class="modal-overlay" @click.self="showCreateModal = false">
      <div class="modal modal-lg">
        <div class="modal-header">
          <h3>➕ 创建新版本</h3>
          <button @click="showCreateModal = false" class="close-btn">✕</button>
        </div>
        <div class="modal-content">
          <!-- 内容来源选择 -->
          <div class="content-mode-tabs">
            <button
              :class="{ active: contentMode === 'edit' }"
              @click="contentMode = 'edit'"
              class="mode-tab"
            >
              ✏️ 编辑文本
            </button>
            <button
              :class="{ active: contentMode === 'upload' }"
              @click="contentMode = 'upload'"
              class="mode-tab"
            >
              📤 从文件上传
            </button>
          </div>

          <!-- 编辑模式 -->
          <div v-if="contentMode === 'edit'" class="mode-content">
            <div class="form-group">
              <label>标题 *</label>
              <input
                v-model="newVersionData.title"
                type="text"
                placeholder="输入版本标题"
                class="form-input"
              />
            </div>
            <div class="form-group">
              <label>内容 * <span class="hint">(可直接编辑或粘贴)</span></label>
              <textarea
                v-model="newVersionData.content"
                placeholder="输入或粘贴文档内容"
                class="form-textarea form-textarea-lg"
              ></textarea>
              <div class="char-count">字数: {{ newVersionData.content.length }}</div>
            </div>
          </div>

          <!-- 上传模式 -->
          <div v-if="contentMode === 'upload'" class="mode-content">
            <div class="upload-area" @click="triggerFileUpload">
              <div class="upload-icon">📄</div>
              <div class="upload-text">
                <p>点击选择文件或拖拽文件到此处</p>
                <p class="hint">支持 .txt, .md 等文本格式</p>
              </div>
              <input
                ref="fileInput"
                type="file"
                accept=".txt,.md,.markdown"
                style="display: none"
                @change="handleFileSelect"
              />
            </div>
            <div v-if="uploadedFile" class="uploaded-file">
              <div class="file-info">
                <span class="file-name">📎 {{ uploadedFile.name }}</span>
                <span class="file-size">({{ (uploadedFile.size / 1024).toFixed(2) }} KB)</span>
              </div>
              <button @click="uploadedFile = null; newVersionData.content = ''" class="btn-small">
                ✕ 移除
              </button>
            </div>
            <div class="form-group">
              <label>标题 *</label>
              <input
                v-model="newVersionData.title"
                type="text"
                placeholder="输入版本标题"
                class="form-input"
              />
            </div>
          </div>

          <!-- 公共字段 -->
          <div class="form-row">
            <div class="form-group">
              <label>变更说明</label>
              <input
                v-model="newVersionData.changeLog"
                type="text"
                placeholder="例如：修复了错别字，补充了第3段"
                class="form-input"
              />
            </div>
            <div class="form-group">
              <label>版本标签</label>
              <input
                v-model="newVersionData.tag"
                type="text"
                placeholder="例如：v1.1, release, draft"
                class="form-input"
              />
            </div>
          </div>
          <div class="form-group">
            <label>编辑者</label>
            <input
              v-model="newVersionData.createdBy"
              type="text"
              placeholder="输入编辑者名称"
              class="form-input"
            />
          </div>
          <div class="modal-actions">
            <button @click="showCreateModal = false" class="btn btn-secondary">取消</button>
            <button @click="createNewVersion" :disabled="creatingVersion" class="btn btn-primary">
              {{ creatingVersion ? '创建中...' : '创建版本' }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped lang="css">
.version-manager {
  background: white;
  border-radius: 8px;
  padding: 20px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
}

.header {
  margin-bottom: 20px;
  border-bottom: 2px solid #f0f0f0;
  padding-bottom: 15px;
}

.header-top {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 15px;
}

.header h2 {
  margin: 0;
  color: #1a73e8;
  font-size: 24px;
}

.tab-buttons {
  display: flex;
  gap: 10px;
}

.tab-btn {
  padding: 8px 16px;
  border: 2px solid #ddd;
  background: white;
  border-radius: 4px;
  cursor: pointer;
  transition: all 0.3s;
}

.tab-btn.active {
  background: #1a73e8;
  color: white;
  border-color: #1a73e8;
}

.tab-content {
  padding: 20px 0;
}

.action-buttons {
  display: flex;
  gap: 10px;
  margin-bottom: 20px;
  flex-wrap: wrap;
}

.btn {
  padding: 8px 16px;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 14px;
  transition: all 0.3s;
}

.btn-primary {
  background: #1a73e8;
  color: white;
}

.btn-primary:hover:not(:disabled) {
  background: #1557b0;
}

.btn-secondary {
  background: #f0f0f0;
  color: #333;
}

.btn-secondary:hover:not(:disabled) {
  background: #e0e0e0;
}

.btn-warning {
  background: #fbbc04;
  color: #000;
}

.btn-warning:hover:not(:disabled) {
  background: #f9ab00;
}

.btn-danger {
  background: #ea4335;
  color: white;
}

.btn-danger:hover:not(:disabled) {
  background: #d33425;
}

.btn-success {
  background: #67c23a;
  color: white;
}

.btn-success:hover:not(:disabled) {
  background: #85ce61;
}

.btn-sm {
  padding: 6px 12px;
  font-size: 12px;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.loading,
.empty {
  text-align: center;
  padding: 40px;
  color: #999;
  font-size: 16px;
}

.versions-table {
  overflow-x: auto;
}

table {
  width: 100%;
  border-collapse: collapse;
  margin-bottom: 20px;
}

thead {
  background: #f5f5f5;
}

th {
  padding: 12px;
  text-align: left;
  font-weight: 600;
  color: #333;
  border-bottom: 2px solid #ddd;
}

td {
  padding: 12px;
  border-bottom: 1px solid #eee;
}

tbody tr:hover {
  background: #f9f9f9;
}

tbody tr.current {
  background: #f0f7ff;
}

.title-cell {
  max-width: 200px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.badge {
  display: inline-block;
  padding: 4px 8px;
  background: #e8f0fe;
  color: #1a73e8;
  border-radius: 4px;
  font-size: 12px;
}

.badge-empty {
  color: #999;
}

.badge-current {
  background: #d4edda;
  color: #155724;
}

.actions {
  display: flex;
  gap: 8px;
  align-items: center;
}

.action-btn {
  background: none;
  border: none;
  cursor: pointer;
  font-size: 16px;
  padding: 4px 8px;
  border-radius: 4px;
  transition: all 0.3s;
}

.action-btn:hover {
  background: #f0f0f0;
}

.action-btn.action-delete:hover {
  background: #ffebee;
}

.export-menu {
  position: relative;
  display: inline-block;
}

.export-options {
  display: none;
  position: absolute;
  top: 100%;
  right: 0;
  background: white;
  border: 1px solid #ddd;
  border-radius: 4px;
  z-index: 10;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
}

.export-menu:hover .export-options {
  display: block;
}

.export-options button {
  display: block;
  width: 100%;
  padding: 8px 16px;
  border: none;
  background: white;
  cursor: pointer;
  text-align: left;
  transition: all 0.3s;
}

.export-options button:hover {
  background: #f0f0f0;
}

.pagination {
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 20px;
  margin-top: 20px;
}

.page-info {
  color: #666;
  font-size: 14px;
}

.compare-section {
  max-width: 1000px;
}

.compare-inputs {
  display: flex;
  gap: 15px;
  margin-bottom: 20px;
  align-items: flex-end;
  flex-wrap: wrap;
}

.compare-inputs div {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.compare-inputs label {
  font-weight: 600;
  color: #333;
  font-size: 14px;
}

.compare-inputs input {
  padding: 8px;
  border: 1px solid #ddd;
  border-radius: 4px;
  width: 100px;
}

.compare-result {
  margin-top: 20px;
}

.stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 15px;
  margin-bottom: 20px;
}

.stat-item {
  display: flex;
  justify-content: space-between;
  padding: 12px;
  background: #f5f5f5;
  border-radius: 4px;
}

.stat-label {
  font-weight: 600;
  color: #333;
}

.stat-value {
  font-weight: bold;
  font-size: 18px;
}

.stat-value.added {
  color: #28a745;
}

.stat-value.removed {
  color: #ea4335;
}

.stat-value.modified {
  color: #fbbc04;
}

.diff-content {
  margin-top: 15px;
}

.diff-content h4 {
  margin: 0 0 10px 0;
  color: #333;
}

.diff-content pre {
  background: #f5f5f5;
  padding: 15px;
  border-radius: 4px;
  max-height: 400px;
  overflow: auto;
  font-size: 12px;
  line-height: 1.5;
}

.statistics-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 15px;
}

.stat-card {
  padding: 20px;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  border-radius: 8px;
  text-align: center;
}

.stat-card:nth-child(2) {
  background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%);
}

.stat-card:nth-child(3) {
  background: linear-gradient(135deg, #4facfe 0%, #00f2fe 100%);
}

.stat-card:nth-child(4) {
  background: linear-gradient(135deg, #43e97b 0%, #38f9d7 100%);
}

.stat-card:nth-child(5) {
  background: linear-gradient(135deg, #fa709a 0%, #fee140 100%);
}

.stat-card:nth-child(6) {
  background: linear-gradient(135deg, #30cfd0 0%, #330867 100%);
}

.stat-card:nth-child(7) {
  background: linear-gradient(135deg, #a8edea 0%, #fed6e3 100%);
  color: #333;
}

.stat-card:nth-child(8) {
  background: linear-gradient(135deg, #ff9a56 0%, #ff6a88 100%);
}

.stat-title {
  font-size: 12px;
  opacity: 0.9;
  margin-bottom: 10px;
}

.stat-value {
  font-size: 24px;
  font-weight: bold;
}

.stat-value.small {
  font-size: 14px;
}

.tags-section {
  grid-column: 1 / -1;
  padding: 20px;
  background: #f5f5f5;
  border-radius: 8px;
}

.tags-section h4 {
  margin: 0 0 10px 0;
  color: #333;
}

.tags-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

/* 模态框样式 */
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal {
  background: white;
  border-radius: 8px;
  max-width: 600px;
  width: 90%;
  max-height: 80vh;
  display: flex;
  flex-direction: column;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.2);
}

.modal-lg {
  max-width: 800px;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 20px;
  border-bottom: 1px solid #eee;
}

.modal-header h3 {
  margin: 0;
  color: #333;
}

.close-btn {
  background: none;
  border: none;
  font-size: 24px;
  cursor: pointer;
  color: #999;
}

.close-btn:hover {
  color: #333;
}

.modal-content {
  padding: 20px;
  overflow-y: auto;
  flex: 1;
}

.modal-text {
  margin: 0 0 15px 0;
  color: #666;
}

.form-group {
  margin-bottom: 15px;
}

.form-group label {
  display: block;
  margin-bottom: 8px;
  font-weight: 600;
  color: #333;
}

.form-group input,
.form-group textarea {
  width: 100%;
  padding: 10px;
  border: 1px solid #ddd;
  border-radius: 4px;
  font-family: inherit;
  font-size: 14px;
  box-sizing: border-box;
}

.form-group input:focus,
.form-group textarea:focus {
  outline: none;
  border-color: #409eff;
  box-shadow: 0 0 0 2px rgba(64, 158, 255, 0.2);
}

.form-group textarea {
  resize: vertical;
  min-height: 120px;
}

.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 15px;
}

.version-meta {
  display: flex;
  gap: 20px;
  margin-bottom: 15px;
  padding: 10px;
  background: #f5f5f5;
  border-radius: 4px;
  font-size: 12px;
  color: #666;
}

.change-log {
  margin-bottom: 15px;
  padding: 10px;
  background: #fffbea;
  border-left: 4px solid #fbbc04;
  border-radius: 4px;
}

.change-log strong {
  color: #333;
}

.change-log p {
  margin: 5px 0 0 0;
  color: #666;
}

.content {
  background: #f5f5f5;
  padding: 15px;
  border-radius: 4px;
  max-height: 400px;
  overflow: auto;
  font-size: 12px;
  line-height: 1.5;
}

.modal-actions {
  display: flex;
  gap: 10px;
  padding: 15px;
  border-top: 1px solid #eee;
  justify-content: flex-end;
}

/* 创建新版本 - 内容模式切换 */
.content-mode-tabs {
  display: flex;
  gap: 10px;
  margin-bottom: 20px;
  border-bottom: 2px solid #eee;
  padding-bottom: 10px;
}

.mode-tab {
  padding: 8px 16px;
  border: none;
  background: none;
  color: #999;
  font-size: 14px;
  cursor: pointer;
  border-bottom: 3px solid transparent;
  transition: all 0.3s;
}

.mode-tab.active {
  color: #409eff;
  border-bottom-color: #409eff;
}

.mode-tab:hover {
  color: #333;
}

.mode-content {
  margin-bottom: 20px;
}

/* 文本编辑模式 */
.form-textarea-lg {
  min-height: 250px;
  font-family: 'Monaco', 'Menlo', 'Ubuntu Mono', monospace;
}

.char-count {
  margin-top: 5px;
  font-size: 12px;
  color: #999;
  text-align: right;
}

/* 文件上传模式 */
.upload-area {
  border: 2px dashed #409eff;
  border-radius: 8px;
  padding: 40px 20px;
  text-align: center;
  cursor: pointer;
  transition: all 0.3s;
  background: rgba(64, 158, 255, 0.05);
  margin-bottom: 20px;
}

.upload-area:hover {
  border-color: #66b1ff;
  background: rgba(64, 158, 255, 0.1);
}

.upload-icon {
  font-size: 48px;
  margin-bottom: 10px;
}

.upload-text p {
  margin: 5px 0;
  color: #666;
}

.upload-text p.hint {
  font-size: 12px;
  color: #999;
}

.uploaded-file {
  background: #f0f9ff;
  border: 1px solid #b3d8ff;
  border-radius: 4px;
  padding: 12px 15px;
  margin-bottom: 20px;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.file-info {
  display: flex;
  align-items: center;
  gap: 8px;
}

.file-name {
  color: #333;
  font-weight: 500;
}

.file-size {
  color: #999;
  font-size: 12px;
}

.btn-small {
  padding: 4px 8px;
  font-size: 12px;
  background: #f0f0f0;
  border: 1px solid #ddd;
  border-radius: 4px;
  cursor: pointer;
  transition: all 0.2s;
}

.btn-small:hover {
  background: #e0e0e0;
}

.hint {
  font-size: 12px;
  color: #999;
  font-weight: normal;
}

@media (max-width: 768px) {
  .versions-table {
    font-size: 12px;
  }

  th,
  td {
    padding: 8px;
  }

  .statistics-grid {
    grid-template-columns: 1fr;
  }

  .compare-inputs {
    flex-direction: column;
  }

  .compare-inputs input {
    width: 100%;
  }

  .actions {
    flex-wrap: wrap;
  }

  .form-row {
    grid-template-columns: 1fr;
  }

  .upload-area {
    padding: 30px 15px;
  }

  .upload-icon {
    font-size: 36px;
  }
}
</style>

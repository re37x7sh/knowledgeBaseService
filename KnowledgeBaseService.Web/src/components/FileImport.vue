<template>
  <el-card class="box-card">
    <template #header>
      <div class="card-header">
        <span class="title">
          <el-icon><Upload /></el-icon>
          导入文档
        </span>
      </div>
    </template>

    <el-tabs>
      <!-- 单文件导入 -->
      <el-tab-pane label="单文件导入">
        <div class="import-section">
          <el-upload
            ref="uploadRef"
            drag
            accept=".docx,.pdf,.md,.txt,.xlsx,.csv,.jsonl,.png,.jpg,.jpeg,.bmp,.gif,.pptx,.ppt"
            :auto-upload="false"
            :on-change="handleFileSelect"
            class="upload-area"
          >
            <el-icon class="el-icon--upload"><upload-filled /></el-icon>
            <div class="el-upload__text">
              拖拽或 <em>点击</em> 上传文件
            </div>
            <template #tip>
              <div class="el-upload__tip">
                支持文档：.docx, .pdf, .md, .txt, .xlsx, .csv, .jsonl, .pptx, .ppt<br>
                支持图片：.png, .jpg, .jpeg, .bmp, .gif（豆包视觉识别）<br>
                单个文件不超过 50MB
              </div>
            </template>
          </el-upload>

          <el-form v-if="selectedFile" class="import-form" label-width="120px">
            <el-form-item label="文件名">
              <span>{{ selectedFile.name }}</span>
            </el-form-item>
            <el-form-item label="文件大小">
              <span>{{ formatFileSize(selectedFile.size) }}</span>
            </el-form-item>
            <el-form-item label="文档标题">
              <el-input v-model="importForm.title" placeholder="输入文档标题" />
            </el-form-item>
            <el-form-item label="分类">
              <el-select v-model="importForm.category" placeholder="选择分类">
                <el-option label="技术文档" value="技术文档" />
                <el-option label="用户指南" value="用户指南" />
                <el-option label="API 文档" value="API 文档" />
                <el-option label="其他" value="其他" />
              </el-select>
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :loading="importing" @click="handleSingleImport">
                开始导入
              </el-button>
              <el-button @click="handleCancelImport">取消</el-button>
            </el-form-item>
          </el-form>
        </div>
      </el-tab-pane>

      <!-- 批量导入 -->
      <el-tab-pane label="批量导入">
        <div class="import-section">
          <el-upload
            ref="batchUploadRef"
            drag
            multiple
            accept=".docx,.pdf,.md,.txt,.xlsx,.csv,.jsonl,.png,.jpg,.jpeg,.bmp,.gif,.pptx,.ppt"
            :auto-upload="false"
            :on-change="handleBatchFilesSelect"
            class="upload-area"
          >
            <el-icon class="el-icon--upload"><upload-filled /></el-icon>
            <div class="el-upload__text">
              拖拽或 <em>点击</em> 上传多个文件
            </div>
            <template #tip>
              <div class="el-upload__tip">
                支持多文件上传，最多 10 个文件，每个不超过 50MB<br>
                支持文档：.docx, .pdf, .md, .txt, .xlsx, .csv, .jsonl, .pptx, .ppt<br>
                支持图片：.png, .jpg, .jpeg, .bmp, .gif
              </div>
            </template>
          </el-upload>

          <div v-if="batchFiles.length > 0" class="batch-preview">
            <el-table :data="batchFiles" style="width: 100%">
              <el-table-column prop="name" label="文件名" show-overflow-tooltip />
              <el-table-column prop="size" label="大小" width="100">
                <template #default="{ row }">
                  {{ formatFileSize(row.size) }}
                </template>
              </el-table-column>
              <el-table-column label="操作" width="80">
                <template #default="{ $index }">
                  <el-button link type="danger" @click="removeBatchFile($index)">
                    删除
                  </el-button>
                </template>
              </el-table-column>
            </el-table>

            <div class="batch-actions">
              <el-button type="primary" :loading="batchImporting" @click="handleBatchImport">
                开始批量导入
              </el-button>
              <el-button @click="handleCancelBatchImport">清空列表</el-button>
            </div>
          </div>
        </div>
      </el-tab-pane>
    </el-tabs>

    <!-- 导入进度显示 -->
    <el-divider v-if="importProgressList.length > 0">导入进度</el-divider>
    <div v-if="importProgressList.length > 0" class="progress-list">
      <div v-for="(progress, index) in importProgressList" :key="index" class="progress-item">
        <div class="progress-header">
          <span class="file-name">{{ progress.fileName }}</span>
          <el-tag :type="getStatusType(progress.status)">
            {{ getStatusLabel(progress.status) }}
          </el-tag>
        </div>
        <el-progress
          :percentage="Math.round(progress.progress)"
          :status="getProgressStatus(progress.status)"
          :show-text="false"
        />
        <div v-if="progress.error" class="error-message">
          <el-icon><CircleCloseFilled /></el-icon>
          {{ progress.error }}
        </div>
      </div>
    </div>
  </el-card>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { Upload as UploadFilled } from '@element-plus/icons-vue'
import { useDocumentStore } from '@/stores/document'
import type { UploadInstance } from 'element-plus'

const documentStore = useDocumentStore()

const uploadRef = ref<UploadInstance>()
const batchUploadRef = ref<UploadInstance>()
const selectedFile = ref<File>()
const batchFiles = ref<File[]>([])
const importing = ref(false)
const batchImporting = ref(false)

const importForm = ref({
  title: '',
  category: '其他',
})

const importProgressList = computed(() => documentStore.getAllProgress())

const handleFileSelect = (uploadFile: any) => {
  selectedFile.value = uploadFile.raw
  importForm.value.title = uploadFile.name.split('.')[0]
}

const handleBatchFilesSelect = (uploadFile: any) => {
  batchFiles.value = Array.from(uploadRef.value?.upload?.fileList || []).map((f: any) => f.raw)
}

const handleSingleImport = async () => {
  if (!selectedFile.value) {
    ElMessage.warning('请选择文件')
    return
  }

  importing.value = true
  try {
    await documentStore.importFile(
      selectedFile.value,
      importForm.value.title || selectedFile.value.name,
      importForm.value.category
    )
    ElMessage.success('文档导入成功')
    handleCancelImport()
  } catch (error) {
    ElMessage.error('导入失败，请重试')
  } finally {
    importing.value = false
  }
}

const handleBatchImport = async () => {
  if (batchFiles.value.length === 0) {
    ElMessage.warning('请选择至少一个文件')
    return
  }

  if (batchFiles.value.length > 10) {
    ElMessage.warning('最多只能导入 10 个文件')
    return
  }

  batchImporting.value = true
  try {
    const result = await documentStore.importFilesBatch(batchFiles.value)
    ElMessage.success(`成功导入 ${result.successCount} 个文件，失败 ${result.failedCount} 个`)
    handleCancelBatchImport()
  } catch (error) {
    ElMessage.error('批量导入失败，请重试')
  } finally {
    batchImporting.value = false
  }
}

const handleCancelImport = () => {
  selectedFile.value = undefined
  importForm.value = { title: '', category: '其他' }
  uploadRef.value?.clearFiles()
}

const handleCancelBatchImport = () => {
  batchFiles.value = []
  batchUploadRef.value?.clearFiles()
}

const removeBatchFile = (index: number) => {
  batchFiles.value.splice(index, 1)
}

const formatFileSize = (bytes: number) => {
  if (bytes === 0) return '0 Bytes'
  const k = 1024
  const sizes = ['Bytes', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i]
}

const getStatusType = (status: string) => {
  const types: Record<string, string> = {
    pending: 'info',
    uploading: 'info',
    indexing: 'warning',
    completed: 'success',
    failed: 'danger',
  }
  return types[status] || 'info'
}

const getStatusLabel = (status: string) => {
  const labels: Record<string, string> = {
    pending: '等待中',
    uploading: '上传中',
    indexing: '索引中',
    completed: '已完成',
    failed: '失败',
  }
  return labels[status] || status
}

const getProgressStatus = (status: string) => {
  if (status === 'failed') return 'exception'
  if (status === 'completed') return 'success'
  return undefined
}
</script>

<style scoped>
.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: bold;
}

.import-section {
  padding: 20px;
}

.upload-area {
  margin-bottom: 20px;
}

.import-form {
  margin-top: 20px;
}

.batch-preview {
  margin-top: 20px;
}

.batch-actions {
  margin-top: 15px;
  display: flex;
  gap: 10px;
}

.progress-list {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.progress-item {
  padding: 12px;
  border: 1px solid #dcdfe4;
  border-radius: 4px;
}

.progress-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.file-name {
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
}

.error-message {
  color: #f56c6c;
  font-size: 12px;
  margin-top: 8px;
  display: flex;
  align-items: center;
  gap: 5px;
}
</style>

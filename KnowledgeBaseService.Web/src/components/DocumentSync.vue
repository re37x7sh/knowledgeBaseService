<template>
  <el-card class="sync-card">
    <template #header>
      <div class="card-header">
        <span class="title">
          <el-icon><Link /></el-icon>
          外部文档同步
        </span>
        <span class="subtitle">调用 /api/documents/sync-content 接口，支持新建或追加内容</span>
      </div>
    </template>

    <el-form
      ref="formRef"
      :model="form"
      :rules="rules"
      label-width="120px"
      class="sync-form"
    >
      <el-form-item label="文档 ID" prop="documentId">
        <el-input
          v-model="form.documentId"
          placeholder="首次同步可留空，成功后可复用返回的 DocumentId"
          clearable
        />
      </el-form-item>

      <el-form-item label="文档名称">
        <el-input v-model="form.name" placeholder="未填写时默认使用“未命名文档”" clearable />
      </el-form-item>

      <el-form-item label="分类">
        <el-input v-model="form.category" placeholder="默认分类：外部同步" clearable />
      </el-form-item>

      <el-form-item label="来源地址">
        <el-input v-model="form.sourceUrl" placeholder="例如：http://example.com/article" clearable />
      </el-form-item>

      <el-form-item label="文件扩展名">
        <el-input v-model="form.fileExtension" placeholder="默认 .json" clearable />
      </el-form-item>

      <el-form-item label="追加分隔符">
        <el-input
          v-model="form.appendDelimiter"
          placeholder="默认换行，可自定义追加内容之间的分隔符"
          clearable
        />
      </el-form-item>

      <el-form-item label="内容" prop="content">
        <el-input
          v-model="form.content"
          type="textarea"
          :rows="10"
          placeholder="请输入需要同步的文本内容"
          :autosize="{ minRows: 6, maxRows: 18 }"
        />
      </el-form-item>

      <el-form-item label="变更说明">
        <el-input v-model="form.changeLog" placeholder="例如：新增 3 段知识点" clearable />
      </el-form-item>

      <el-form-item label="更新者">
        <el-input v-model="form.updatedBy" placeholder="默认 external-api" clearable />
      </el-form-item>

      <el-form-item label="版本标签">
        <el-input v-model="form.tag" placeholder="默认 external" clearable />
      </el-form-item>

      <el-form-item>
        <el-button type="primary" :loading="submitting" @click="handleSubmit">
          <el-icon><Upload /></el-icon>
          提交同步
        </el-button>
        <el-button @click="handleReset" :disabled="submitting">重置</el-button>
      </el-form-item>
    </el-form>

    <el-alert
      v-if="result"
      :title="result.message"
      type="success"
      show-icon
      class="result-alert"
    >
      <template #default>
        <div class="result-content">
          <el-descriptions :column="1" border>
            <el-descriptions-item label="DocumentId">
              <span class="doc-id">{{ result.documentId }}</span>
              <el-button text type="primary" size="small" @click="copyDocumentId">
                复制
              </el-button>
            </el-descriptions-item>
            <el-descriptions-item label="文档名称">{{ result.name }}</el-descriptions-item>
            <el-descriptions-item label="是否新建">{{ result.created ? '是' : '否' }}</el-descriptions-item>
            <el-descriptions-item label="当前版本">v{{ result.version }}</el-descriptions-item>
            <el-descriptions-item label="内容长度">{{ result.contentLength }} 字符</el-descriptions-item>
            <el-descriptions-item label="分类">{{ result.category }}</el-descriptions-item>
            <el-descriptions-item label="最近更新时间">{{ formatDate(result.updatedAt) }}</el-descriptions-item>
          </el-descriptions>
        </div>
      </template>
    </el-alert>
  </el-card>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import { Link, Upload } from '@element-plus/icons-vue'
import { useDocumentStore } from '@/stores/document'
import type {
  UpsertDocumentContentRequest,
  UpsertDocumentContentResponse,
} from '@/types/document'

const documentStore = useDocumentStore()

const form = reactive({
  documentId: '',
  name: '',
  content: '',
  category: '',
  sourceUrl: '',
  appendDelimiter: '',
  changeLog: '',
  updatedBy: '',
  tag: '',
  fileExtension: '',
})

const rules = reactive<FormRules<UpsertDocumentContentRequest>>({
  content: [
    { required: true, message: '请填写需要同步的内容', trigger: 'blur' },
    { min: 1, message: '内容不能为空', trigger: 'blur' },
  ],
})

const formRef = ref<FormInstance>()
const submitting = ref(false)
const result = ref<UpsertDocumentContentResponse | null>(null)

const formatDate = (value: string) => {
  return new Date(value).toLocaleString('zh-CN')
}

const buildPayload = (): UpsertDocumentContentRequest => {
  const payload: UpsertDocumentContentRequest = {
    content: form.content,
  }

  if (form.documentId.trim()) payload.documentId = form.documentId.trim()
  if (form.name.trim()) payload.name = form.name.trim()
  if (form.category.trim()) payload.category = form.category.trim()
  if (form.sourceUrl.trim()) payload.sourceUrl = form.sourceUrl.trim()
  if (form.appendDelimiter !== '') payload.appendDelimiter = form.appendDelimiter
  if (form.changeLog.trim()) payload.changeLog = form.changeLog.trim()
  if (form.updatedBy.trim()) payload.updatedBy = form.updatedBy.trim()
  if (form.tag.trim()) payload.tag = form.tag.trim()
  if (form.fileExtension.trim()) payload.fileExtension = form.fileExtension.trim()

  return payload
}

const handleSubmit = () => {
  if (!formRef.value) return

  formRef.value.validate(async (valid) => {
    if (!valid) {
      ElMessage.warning('请完善必填信息')
      return
    }

    submitting.value = true
    try {
      const payload = buildPayload()
      const response = await documentStore.syncDocumentContent(payload)
      result.value = response
      if (!form.documentId && response.documentId) {
        form.documentId = response.documentId
      }
      ElMessage.success(response.message)
    } catch (error) {
      const message = error instanceof Error ? error.message : '同步失败'
      ElMessageBox.alert(message, '同步失败', { type: 'error' })
    } finally {
      submitting.value = false
    }
  })
}

const handleReset = () => {
  if (submitting.value) return
  result.value = null
  form.documentId = ''
  form.name = ''
  form.content = ''
  form.category = ''
  form.sourceUrl = ''
  form.appendDelimiter = ''
  form.changeLog = ''
  form.updatedBy = ''
  form.tag = ''
  form.fileExtension = ''
}

const copyDocumentId = async () => {
  if (!result.value?.documentId) return
  try {
    await navigator.clipboard.writeText(result.value.documentId)
    ElMessage.success('DocumentId 已复制')
  } catch (error) {
    console.error('Clipboard copy failed:', error)
    ElMessage.warning('复制失败，请手动复制')
  }
}
</script>

<style scoped>
.sync-card {
  max-width: 900px;
  margin: 0 auto;
}

.card-header {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 18px;
  font-weight: bold;
}

.subtitle {
  font-size: 13px;
  color: #909399;
}

.sync-form {
  margin-top: 10px;
}

.result-alert {
  margin-top: 20px;
}

.result-content {
  margin-top: 10px;
}

.doc-id {
  font-family: 'Courier New', Courier, monospace;
  margin-right: 8px;
}
</style>

<template>
  <el-card class="box-card">
    <template #header>
      <div class="card-header">
        <span class="title">
          <el-icon><List /></el-icon>
          文档列表
        </span>
        <div class="actions">
          <el-input
            v-model="searchText"
            placeholder="搜索文档"
            style="width: 200px"
            clearable
          />
          <el-button @click="refreshDocuments" :loading="loading">
            <el-icon><Refresh /></el-icon>
            刷新
          </el-button>
        </div>
      </div>
    </template>

    <el-table
      :data="filteredDocuments"
      stripe
      style="width: 100%"
      :loading="loading"
      empty-text="暂无文档"
    >
      <el-table-column prop="title" label="文档标题" min-width="200" show-overflow-tooltip />
      <el-table-column prop="category" label="分类" width="120" />
      <el-table-column label="格式" width="100">
        <template #default="{ row }">
          <el-tag v-if="row.fileExtension" size="small" type="info">
            {{ row.fileExtension }}
          </el-tag>
          <span v-else style="color: #909399; font-size: 12px;">未知</span>
        </template>
      </el-table-column>
      <el-table-column label="创建时间" width="180">
        <template #default="{ row }">
          {{ formatDate(row.createdAt) }}
        </template>
      </el-table-column>
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag type="success">已就绪</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="300" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" size="small" @click="viewDocument(row)">
            查看
          </el-button>
          <el-button link type="success" size="small" @click="manageVersions(row)">
            📋 版本
          </el-button>
          <el-button link type="info" size="small" @click="queryDocument(row)">
            查询
          </el-button>
          <el-popconfirm
            title="确定删除该文档吗？"
            confirm-button-text="确定"
            cancel-button-text="取消"
            @confirm="deleteDocument(row.id)"
          >
            <template #reference>
              <el-button link type="danger" size="small">删除</el-button>
            </template>
          </el-popconfirm>
        </template>
      </el-table-column>
    </el-table>

    <!-- 分页 -->
    <div class="pagination">
      <el-pagination
        v-model:current-page="currentPage"
        v-model:page-size="pageSize"
        :page-sizes="[5, 10, 20, 50]"
        :total="documentStore.total"
        layout="total, sizes, prev, pager, next, jumper"
        @change="handlePaginationChange"
      />
    </div>

    <!-- 查看文档对话框 -->
    <el-dialog v-model="dialogVisible" :title="currentDocument?.title" width="70%">
      <div v-if="currentDocument" class="document-view">
        <div class="doc-meta">
          <span><strong>分类:</strong> {{ currentDocument.category }}</span>
          <span><strong>创建时间:</strong> {{ formatDate(currentDocument.createdAt) }}</span>
        </div>
        <div v-if="documentLoading" class="doc-loading">正在加载内容...</div>
        <div v-else class="doc-content">
          {{ currentDocument.content?.substring(0, 1000) || '暂无内容' }}
          <p v-if="(currentDocument.content?.length || 0) > 1000" class="ellipsis">...</p>
        </div>
      </div>
    </el-dialog>
  </el-card>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useDocumentStore } from '@/stores/document'
import { documentApi } from '@/api/document'
import type { DocumentResponse } from '@/types/document'

const documentStore = useDocumentStore()

const searchText = ref('')
const currentPage = ref(1)
const pageSize = ref(10)
const dialogVisible = ref(false)
const currentDocument = ref<DocumentResponse>()
const documentLoading = ref(false)

const loading = computed(() => documentStore.loading)

const filteredDocuments = computed(() => {
  if (!searchText.value) {
    return documentStore.sortedDocuments
  }
  return documentStore.sortedDocuments.filter((doc) =>
    doc.title.toLowerCase().includes(searchText.value.toLowerCase())
  )
})

const formatDate = (date: string) => {
  return new Date(date).toLocaleString('zh-CN')
}

const refreshDocuments = async () => {
  try {
    await documentStore.fetchDocuments((currentPage.value - 1) * pageSize.value, pageSize.value)
  } catch (error) {
    ElMessage.error('刷新文档列表失败')
  }
}

const handlePaginationChange = () => {
  refreshDocuments()
}

const viewDocument = async (doc: DocumentResponse) => {
  dialogVisible.value = true
  currentDocument.value = doc
  documentLoading.value = true
  try {
    const fullDocument = await documentApi.getDocument(doc.id)
    currentDocument.value = fullDocument
  } catch (error) {
    ElMessage.error('加载文档内容失败')
    console.error('Failed to load document:', error)
  } finally {
    documentLoading.value = false
  }
}

const queryDocument = (doc: DocumentResponse) => {
  // 触发自定义事件，让父组件处理查询
  emit('query-document', doc)
}

const manageVersions = (doc: DocumentResponse) => {
  // 触发管理版本事件
  emit('manage-versions', doc)
}

const deleteDocument = async (id: string) => {
  try {
    await documentStore.deleteDocument(id)
    ElMessage.success('文档已删除')
    refreshDocuments()
  } catch (error) {
    ElMessage.error('删除文档失败')
  }
}

const emit = defineEmits<{
  'query-document': [doc: DocumentResponse]
  'manage-versions': [doc: DocumentResponse]
}>()

onMounted(() => {
  refreshDocuments()
})
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

.actions {
  display: flex;
  gap: 10px;
  align-items: center;
}

.pagination {
  display: flex;
  justify-content: center;
  margin-top: 20px;
  padding: 20px 0;
}

.document-view {
  padding: 20px;
}

.doc-meta {
  display: flex;
  gap: 20px;
  margin-bottom: 20px;
  padding-bottom: 10px;
  border-bottom: 1px solid #dcdfe4;
  color: #606266;
  font-size: 14px;
}

.doc-content {
  line-height: 1.6;
  color: #303133;
  max-height: 400px;
  overflow-y: auto;
  white-space: pre-wrap;
  word-break: break-word;
}

.doc-loading {
  color: #909399;
  text-align: center;
  padding: 40px 0;
  font-size: 14px;
}

.ellipsis {
  text-align: center;
  color: #909399;
  margin-top: 10px;
}
</style>

<template>
  <div class="main-layout">
    <el-container>
      <!-- 头部 -->
      <el-header class="app-header">
        <div class="header-content">
          <div class="logo">
            <el-icon style="font-size: 28px"><DataAnalysis /></el-icon>
            <h1>知识库服务</h1>
          </div>
          <div class="header-actions">
            <el-badge :value="documentStore.total" class="item">
              <el-button text>文档 {{ documentStore.total }}</el-button>
            </el-badge>
          </div>
        </div>
      </el-header>

      <el-container>
        <!-- 侧边栏 -->
        <el-aside width="200px" class="sidebar">
          <el-menu :default-active="activeTab" @select="handleMenuSelect">
            <el-menu-item index="import">
              <el-icon><Upload /></el-icon>
              <span>导入文档</span>
            </el-menu-item>
            <el-menu-item index="documents">
              <el-icon><List /></el-icon>
              <span>文档列表</span>
            </el-menu-item>
            <el-menu-item index="sync">
              <el-icon><Link /></el-icon>
              <span>外部同步</span>
            </el-menu-item>
            <el-menu-item index="versions">
              <el-icon><DocumentCopy /></el-icon>
              <span>版本管理</span>
            </el-menu-item>
            <el-menu-item index="chat">
              <el-icon><ChatDotRound /></el-icon>
              <span>RAG 对话</span>
            </el-menu-item>
          </el-menu>
        </el-aside>

        <!-- 主要内容 -->
        <el-main class="main-content">
          <!-- 导入文档页面 -->
          <div v-if="activeTab === 'import'" class="tab-content">
            <FileImport />
          </div>

          <!-- 文档列表页面 -->
          <div v-if="activeTab === 'documents'" class="tab-content">
            <DocumentList 
              @query-document="handleQueryDocument"
              @manage-versions="handleManageVersions"
            />
          </div>

          <!-- 外部同步页面 -->
          <div v-if="activeTab === 'sync'" class="tab-content">
            <DocumentSync />
          </div>

          <!-- 版本管理页面 -->
          <div v-if="activeTab === 'versions'" class="tab-content">
            <VersionManager 
              v-if="selectedDocumentId"
              :document-id="selectedDocumentId"
              :document-title="selectedDocumentTitle"
              @back="handleBackFromVersions"
            />
            <div v-else class="empty-state">
              <el-empty description="请先从文档列表中选择一个文档">
                <el-button type="primary" @click="activeTab = 'documents'">
                  前往文档列表
                </el-button>
              </el-empty>
            </div>
          </div>

          <!-- RAG 查询页面 -->
          <div v-if="activeTab === 'chat'" class="tab-content">
            <RAGChat />
          </div>
        </el-main>
      </el-container>
    </el-container>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useDocumentStore } from '@/stores/document'
import { useChatStore } from '@/stores/chat'
import FileImport from '@/components/FileImport.vue'
import DocumentList from '@/components/DocumentList.vue'
import RAGChat from '@/components/RAGChat.vue'
import VersionManager from '@/components/VersionManager.vue'
import DocumentSync from '@/components/DocumentSync.vue'
import type { DocumentResponse } from '@/types/document'

const documentStore = useDocumentStore()
const chatStore = useChatStore()

const activeTab = ref('import')
const selectedDocumentId = ref<string>('')
const selectedDocumentTitle = ref<string>('')

const handleMenuSelect = (index: string) => {
  activeTab.value = index
}

const handleQueryDocument = (doc: DocumentResponse) => {
  // 切换到对话页面，并预设查询内容
  activeTab.value = 'chat'
  chatStore.addMessage('user', `请总结关于"${doc.title}"的主要内容`)
  // 同时保存选中的文档用于版本管理
  selectedDocumentId.value = doc.id
  selectedDocumentTitle.value = doc.title
}

const handleManageVersions = (doc: DocumentResponse) => {
  // 切换到版本管理页面
  selectedDocumentId.value = doc.id
  selectedDocumentTitle.value = doc.title
  activeTab.value = 'versions'
}

const handleBackFromVersions = () => {
  // 从版本管理返回到文档列表
  activeTab.value = 'documents'
}

onMounted(() => {
  // 初始化加载文档列表
  documentStore.fetchDocuments(0, 10)
})
</script>

<style scoped>
.main-layout {
  height: 100vh;
  display: flex;
  flex-direction: column;
}

.app-header {
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  padding: 0;
  display: flex;
  align-items: center;
  box-shadow: 0 2px 12px 0 rgba(0, 0, 0, 0.1);
}

.header-content {
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
  padding: 0 20px;
}

.logo {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 20px;
  font-weight: bold;
}

.logo h1 {
  margin: 0;
  font-size: 24px;
}

.header-actions {
  display: flex;
  gap: 15px;
  align-items: center;
}

.el-container {
  flex: 1;
  display: flex;
}

.sidebar {
  background-color: #f5f7fa;
  border-right: 1px solid #dcdfe4;
}

.main-content {
  padding: 20px;
  overflow-y: auto;
  flex: 1;
}

.tab-content {
  animation: fadeIn 0.3s ease-in-out;
}

@keyframes fadeIn {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>

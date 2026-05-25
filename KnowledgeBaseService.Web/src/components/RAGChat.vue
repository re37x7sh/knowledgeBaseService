<template>
  <el-card class="box-card">
    <template #header>
      <div class="card-header">
        <span class="title">
          <el-icon><ChatDotRound /></el-icon>
          RAG 查询与对话
        </span>
        <el-button link type="danger" @click="clearChat">
          <el-icon><Delete /></el-icon>
          清空对话
        </el-button>
      </div>
    </template>

    <div class="chat-container">
      <!-- 消息列表 -->
      <div ref="messagesContainer" class="messages">
        <div v-if="chatStore.messages.length === 0" class="empty-state">
          <el-empty description="暂无对话，开始提问吧" />
        </div>

        <div v-for="message in chatStore.messages" :key="message.id" class="message-item">
          <div :class="['message', message.role]">
            <div class="message-avatar">
              <el-icon v-if="message.role === 'user'">
                <User />
              </el-icon>
              <el-icon v-else>
                <Robot />
              </el-icon>
            </div>

            <div class="message-content">
              <div class="message-text">
                {{ message.content }}
              </div>

              <!-- 显示来源文档 -->
              <div v-if="message.sources && message.sources.length > 0" class="sources">
                <el-divider direction="horizontal" />
                <div class="sources-title">📚 相关文档</div>
                <div v-for="(source, index) in message.sources" :key="index" class="source-item">
                  <div class="source-header">
                    <span class="source-title">{{ source.title }}</span>
                    <el-tag v-if="source.fileType === 'image'" type="success" size="small">
                      🖼️ 图片
                    </el-tag>
                    <el-tag size="small">
                      {{ (source.score * 100).toFixed(1) }}% 相关
                    </el-tag>
                  </div>
                  
                  <!-- 如果是图片，显示图片内容 -->
                  <div v-if="source.fileType === 'image' && source.imageBase64" class="source-image-container">
                    <img 
                      :src="`data:image/jpeg;base64,${source.imageBase64}`" 
                      class="source-image"
                      :alt="source.title"
                    />
                    <div v-if="source.matchHint" class="match-hint">
                      ✅ 命中内容：{{ source.matchHint }}
                    </div>
                  </div>
                  
                  <!-- 文本摘要 -->
                  <div class="source-excerpt">
                    {{ source.snippet }}
                  </div>
                </div>
              </div>

              <!-- 操作按钮 -->
              <div class="message-actions">
                <el-button link size="small" @click="copyMessage(message.content)">
                  复制
                </el-button>
                <el-button link size="small" type="danger" @click="deleteMessage(message.id)">
                  删除
                </el-button>
              </div>
            </div>
          </div>
        </div>

        <!-- 加载指示 -->
        <div v-if="chatStore.loading || chatStore.streaming" class="loading-indicator">
          <el-icon class="is-loading"><Loading /></el-icon>
          <span>正在思考中...</span>
        </div>
      </div>

      <!-- 输入区域 -->
      <div class="input-area">
        <!-- 文档过滤选择 -->
        <div class="filter-bar">
          <span class="filter-label">📄 搜索范围:</span>
          <el-select
            v-model="selectedDocumentIds"
            multiple
            filterable
            clearable
            :disabled="chatStore.loading || chatStore.streaming"
            placeholder="不选择则搜索全库，选择后仅在该文档中搜索"
            style="flex: 1; margin: 0 10px"
          >
            <el-option
              v-for="doc in documentStore.documents"
              :key="doc.id"
              :label="`${doc.title} (${doc.category})`"
              :value="doc.id"
            />
          </el-select>
          <span v-if="selectedDocumentIds.length > 0" class="filter-tip">
            已选择 {{ selectedDocumentIds.length }} 个文档
          </span>
        </div>

        <el-input-group>
          <el-input
            v-model="inputQuery"
            type="textarea"
            placeholder="输入你的问题..."
            :rows="3"
            :disabled="chatStore.loading || chatStore.streaming"
            @keydown.ctrl.enter="submitQuery"
            @keydown.meta.enter="submitQuery"
          />
        </el-input-group>

        <div class="input-footer">
          <div class="checkbox-group">
            <el-checkbox v-model="useStream" :disabled="chatStore.loading || chatStore.streaming">
              使用流式响应
            </el-checkbox>
            <el-divider direction="vertical" />
            <el-checkbox v-model="enableHybridMode" :disabled="chatStore.loading || chatStore.streaming">
              <span class="hybrid-mode-label">
                🚀 混合模式
                <el-tooltip content="优先基于知识库回答，若知识库信息不足，AI 会自动补充通用知识" placement="top">
                  <el-icon style="margin-left: 4px; cursor: help"><InfoFilled /></el-icon>
                </el-tooltip>
              </span>
            </el-checkbox>
          </div>
          <div class="input-actions">
            <el-button @click="inputQuery = ''" :disabled="chatStore.loading || chatStore.streaming">
              清空
            </el-button>
            <el-button
              type="primary"
              :loading="chatStore.loading || chatStore.streaming"
              @click="submitQuery"
            >
              <el-icon><Send /></el-icon>
              提交查询
            </el-button>
          </div>
        </div>
      </div>
    </div>
  </el-card>
</template>

<script setup lang="ts">
import { ref, computed, nextTick, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { InfoFilled } from '@element-plus/icons-vue'
import { useChatStore } from '@/stores/chat'
import { useDocumentStore } from '@/stores/document'

const chatStore = useChatStore()
const documentStore = useDocumentStore()
const messagesContainer = ref<HTMLElement>()
const inputQuery = ref('')
const useStream = ref(false)
const selectedDocumentIds = ref<string[]>([])
const enableHybridMode = ref(false)  // 混合模式开关：优先知识库，不足时补充通用知识

const submitQuery = async () => {
  if (!inputQuery.value.trim()) {
    ElMessage.warning('请输入查询内容')
    return
  }

  const query = inputQuery.value.trim()
  inputQuery.value = ''

  try {
    if (useStream.value) {
      await chatStore.queryStream(query, selectedDocumentIds.value.length > 0 ? selectedDocumentIds.value : undefined, enableHybridMode.value)
    } else {
      await chatStore.query(query, selectedDocumentIds.value.length > 0 ? selectedDocumentIds.value : undefined, enableHybridMode.value)
    }
    // 自动滚动到底部
    await nextTick()
    scrollToBottom()
  } catch (error) {
    ElMessage.error('查询失败，请重试')
  }
}

const clearChat = () => {
  chatStore.clearMessages()
}

const deleteMessage = (id: string) => {
  chatStore.deleteMessage(id)
}

const copyMessage = async (content: string) => {
  try {
    if (navigator.clipboard) {
      await navigator.clipboard.writeText(content)
      ElMessage.success('已复制')
    } else {
      // Fallback for older browsers
      const textarea = document.createElement('textarea')
      textarea.value = content
      document.body.appendChild(textarea)
      textarea.select()
      document.execCommand('copy')
      document.body.removeChild(textarea)
      ElMessage.success('已复制')
    }
  } catch (error) {
    ElMessage.error('复制失败，请手动复制')
  }
}

const scrollToBottom = () => {
  if (messagesContainer.value) {
    messagesContainer.value.scrollTop = messagesContainer.value.scrollHeight
  }
}

onMounted(() => {
  // 初始化时滚动到底部
  nextTick(() => {
    scrollToBottom()
  })
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

.chat-container {
  display: flex;
  flex-direction: column;
  height: 700px;
  gap: 15px;
}

.messages {
  flex: 1;
  overflow-y: auto;
  padding: 15px;
  background-color: #f5f7fa;
  border-radius: 4px;
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.empty-state {
  display: flex;
  justify-content: center;
  align-items: center;
  height: 100%;
}

.message-item {
  display: flex;
  animation: slideIn 0.3s ease-out;
}

@keyframes slideIn {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.message {
  display: flex;
  gap: 10px;
  max-width: 70%;
}

.message.user {
  margin-left: auto;
  flex-direction: row-reverse;
}

.message.assistant {
  margin-right: auto;
}

.message-avatar {
  flex-shrink: 0;
  width: 36px;
  height: 36px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 20px;
}

.message.user .message-avatar {
  background-color: #e0f2f1;
  color: #00897b;
}

.message.assistant .message-avatar {
  background-color: #f3e5f5;
  color: #7b1fa2;
}

.message-content {
  background: white;
  border-radius: 8px;
  padding: 12px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
}

.message.user .message-content {
  background: #e0f7fa;
}

.message-text {
  color: #303133;
  line-height: 1.6;
  word-wrap: break-word;
  overflow-wrap: break-word;
  white-space: pre-wrap;
}

.sources {
  margin-top: 10px;
}

.sources-title {
  font-weight: 600;
  color: #606266;
  margin: 10px 0 8px 0;
  font-size: 13px;
}

.source-item {
  background: #fafbfc;
  border-left: 3px solid #409eff;
  padding: 8px 10px;
  margin: 6px 0;
  border-radius: 2px;
}

.source-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 4px;
  gap: 8px;
}

.source-title {
  font-weight: 500;
  color: #303133;
  font-size: 13px;
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.source-excerpt {
  font-size: 12px;
  color: #606266;
  overflow: hidden;
  text-overflow: ellipsis;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

.source-image-container {
  margin: 10px 0;
  border: 1px solid #e4e7ed;
  border-radius: 4px;
  overflow: hidden;
  background: #fff;
}

.source-image {
  max-width: 100%;
  height: auto;
  display: block;
  cursor: pointer;
  transition: transform 0.3s ease;
}

.source-image:hover {
  transform: scale(1.02);
}

.match-hint {
  padding: 8px 12px;
  background: #f0f9ff;
  border-top: 1px solid #e4e7ed;
  font-size: 12px;
  color: #409eff;
  line-height: 1.5;
}

.message-actions {
  display: flex;
  gap: 8px;
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px solid #ebeef5;
}

.loading-indicator {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  color: #606266;
}

.is-loading {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  0% {
    transform: rotate(0deg);
  }
  100% {
    transform: rotate(360deg);
  }
}

.input-area {
  border-top: 1px solid #dcdfe4;
  padding-top: 15px;
}

.input-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 10px;
}

.input-actions {
  display: flex;
  gap: 10px;
}

.filter-bar {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px;
  background-color: #f5f7fa;
  border-radius: 4px;
  margin-bottom: 10px;
}

.filter-label {
  font-weight: 500;
  color: #606266;
  flex-shrink: 0;
}

.filter-tip {
  color: #409eff;
  font-size: 12px;
  flex-shrink: 0;
}

.checkbox-group {
  display: flex;
  align-items: center;
  gap: 12px;
}

.hybrid-mode-label {
  display: flex;
  align-items: center;
  gap: 4px;
}
</style>

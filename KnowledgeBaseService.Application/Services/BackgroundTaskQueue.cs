using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Core.Entities;

namespace KnowledgeBaseService.Application.Services
{
    /// <summary>
    /// 后台任务队列实现
    /// </summary>
    public class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly Channel<DocumentImportTask> _queue;
        private readonly ConcurrentDictionary<Guid, DocumentImportTask> _taskStore;

        public BackgroundTaskQueue(int capacity = 100)
        {
            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            };
            _queue = Channel.CreateBounded<DocumentImportTask>(options);
            _taskStore = new ConcurrentDictionary<Guid, DocumentImportTask>();
        }

        /// <summary>
        /// 将文档导入任务加入队列
        /// </summary>
        public async ValueTask QueueImportTaskAsync(DocumentImportTask task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            // 保存任务到存储
            _taskStore[task.TaskId] = task;

            // 加入队列
            await _queue.Writer.WriteAsync(task);
        }

        /// <summary>
        /// 从队列中取出任务
        /// </summary>
        public async ValueTask<DocumentImportTask?> DequeueAsync(CancellationToken cancellationToken)
        {
            var task = await _queue.Reader.ReadAsync(cancellationToken);
            return task;
        }

        /// <summary>
        /// 获取任务状态
        /// </summary>
        public DocumentImportTask? GetTaskStatus(Guid taskId)
        {
            _taskStore.TryGetValue(taskId, out var task);
            return task;
        }

        /// <summary>
        /// 更新任务状态
        /// </summary>
        public void UpdateTaskStatus(DocumentImportTask task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            _taskStore[task.TaskId] = task;
        }
    }
}

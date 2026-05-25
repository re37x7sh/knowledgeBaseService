using System;
using System.Threading;
using System.Threading.Tasks;
using KnowledgeBaseService.Core.Entities;

namespace KnowledgeBaseService.Application.Interfaces
{
    /// <summary>
    /// 后台任务队列接口
    /// </summary>
    public interface IBackgroundTaskQueue
    {
        /// <summary>
        /// 将文档导入任务加入队列
        /// </summary>
        ValueTask QueueImportTaskAsync(DocumentImportTask task);

        /// <summary>
        /// 从队列中取出任务
        /// </summary>
        ValueTask<DocumentImportTask?> DequeueAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 获取任务状态
        /// </summary>
        DocumentImportTask? GetTaskStatus(Guid taskId);

        /// <summary>
        /// 更新任务状态
        /// </summary>
        void UpdateTaskStatus(DocumentImportTask task);
    }
}

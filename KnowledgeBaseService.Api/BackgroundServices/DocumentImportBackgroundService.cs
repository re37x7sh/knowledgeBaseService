using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Application.Services;
using KnowledgeBaseService.Application.DTOs;
using KnowledgeBaseService.Core.Entities;
using KnowledgeBaseService.Infrastructure.Repositories;

namespace KnowledgeBaseService.Api.BackgroundServices
{
    /// <summary>
    /// 文档导入后台服务
    /// </summary>
    public class DocumentImportBackgroundService : BackgroundService
    {
        private readonly ILogger<DocumentImportBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IBackgroundTaskQueue _taskQueue;

        public DocumentImportBackgroundService(
            ILogger<DocumentImportBackgroundService> logger,
            IServiceProvider serviceProvider,
            IBackgroundTaskQueue taskQueue)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _taskQueue = taskQueue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("文档导入后台服务已启动");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 从队列中取出任务
                    var task = await _taskQueue.DequeueAsync(stoppingToken);
                    
                    if (task != null)
                    {
                        _logger.LogInformation("开始处理导入任务: {TaskId}, 文件: {FileName}", 
                            task.TaskId, task.FileName);

                        // 更新任务状态为处理中
                        task.Status = ImportTaskStatus.Processing;
                        task.StartedAt = DateTime.UtcNow;
                        task.ProgressMessage = "开始处理文件";
                        _taskQueue.UpdateTaskStatus(task);

                        // 在新的作用域中处理任务
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            await ProcessImportTaskAsync(task, scope.ServiceProvider, stoppingToken);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // 服务停止
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理导入任务时发生错误");
                }
            }

            _logger.LogInformation("文档导入后台服务已停止");
        }

        private async Task ProcessImportTaskAsync(
            DocumentImportTask task, 
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken)
        {
            try
            {
                var fileImportService = serviceProvider.GetRequiredService<IFileImportService>();
                var documentService = serviceProvider.GetRequiredService<IDocumentService>();
                var ragService = serviceProvider.GetRequiredService<IRAGService>();

                // 1. 提取文本内容（带进度回调）
                task.Progress = 10;
                task.ProgressMessage = "正在提取文件内容";
                _taskQueue.UpdateTaskStatus(task);

                string content;
                using (var fileStream = File.OpenRead(task.FilePath))
                {
                    content = await fileImportService.ExtractTextAsync(
                        fileStream, 
                        task.FileName, 
                        cancellationToken);
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    throw new InvalidOperationException("文件内容为空或无法解析");
                }

                task.Progress = 40;
                task.ProgressMessage = $"内容提取完成，共 {content.Length} 字符";
                _taskQueue.UpdateTaskStatus(task);

                // 2. 更新文档内容（文档已在接口中创建）
                task.Progress = 50;
                task.ProgressMessage = "正在更新文档内容";
                _taskQueue.UpdateTaskStatus(task);

                if (string.IsNullOrEmpty(task.DocumentId))
                {
                    throw new InvalidOperationException("任务缺少文档ID");
                }

                // 获取并更新文档
                var document = await documentService.GetInternalAsync(task.DocumentId, cancellationToken);
                if (document == null)
                {
                    throw new InvalidOperationException($"文档不存在: {task.DocumentId}");
                }

                // 更新文档内容
                document.Content = content;
                document.UpdatedAt = DateTime.UtcNow;
                
                // 使用仓储更新
                var documentRepository = serviceProvider.GetRequiredService<IDocumentRepository>();
                var updateResult = await documentRepository.UpdateAsync(document);
                
                if (!updateResult)
                {
                    throw new InvalidOperationException("文档内容更新失败");
                }

                task.Progress = 60;
                task.ProgressMessage = "文档内容已更新，正在索引";
                _taskQueue.UpdateTaskStatus(task);

                // 3. 索引文档
                var title = Path.GetFileNameWithoutExtension(task.FileName);
                
                await ragService.IndexDocumentAsync(
                    task.DocumentId,
                    content,
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "title", title },
                        { "category", task.Category ?? "导入文档" },
                        { "sourceFileName", task.FileName },
                        { "fileSize", task.FileSize }
                    },
                    cancellationToken);

                task.Progress = 100;
                task.ProgressMessage = "导入完成";
                task.Status = ImportTaskStatus.Completed;
                task.CompletedAt = DateTime.UtcNow;
                _taskQueue.UpdateTaskStatus(task);

                _logger.LogInformation("导入任务完成: {TaskId}, 文档ID: {DocumentId}", 
                    task.TaskId, task.DocumentId);

                // 清理临时文件
                try
                {
                    if (File.Exists(task.FilePath))
                    {
                        File.Delete(task.FilePath);
                        _logger.LogInformation("已删除临时文件: {FilePath}", task.FilePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "删除临时文件失败: {FilePath}", task.FilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理导入任务失败: {TaskId}, 文件: {FileName}", 
                    task.TaskId, task.FileName);

                task.Status = ImportTaskStatus.Failed;
                task.ErrorMessage = ex.Message;
                task.CompletedAt = DateTime.UtcNow;
                _taskQueue.UpdateTaskStatus(task);

                // 清理临时文件
                try
                {
                    if (File.Exists(task.FilePath))
                    {
                        File.Delete(task.FilePath);
                    }
                }
                catch { }
            }
        }
    }
}

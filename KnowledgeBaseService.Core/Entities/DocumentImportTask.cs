using System;

namespace KnowledgeBaseService.Core.Entities
{
    /// <summary>
    /// 文档导入任务状态
    /// </summary>
    public enum ImportTaskStatus
    {
        /// <summary>
        /// 等待处理
        /// </summary>
        Pending = 0,
        
        /// <summary>
        /// 正在处理
        /// </summary>
        Processing = 1,
        
        /// <summary>
        /// 已完成
        /// </summary>
        Completed = 2,
        
        /// <summary>
        /// 失败
        /// </summary>
        Failed = 3
    }

    /// <summary>
    /// 文档导入任务
    /// </summary>
    public class DocumentImportTask
    {
        /// <summary>
        /// 任务ID
        /// </summary>
        public Guid TaskId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// 文件名
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// 文件保存路径
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 分类
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// 任务状态
        /// </summary>
        public ImportTaskStatus Status { get; set; } = ImportTaskStatus.Pending;

        /// <summary>
        /// 进度百分比（0-100）
        /// </summary>
        public int Progress { get; set; } = 0;

        /// <summary>
        /// 进度描述
        /// </summary>
        public string? ProgressMessage { get; set; }

        /// <summary>
        /// 创建的文档ID
        /// </summary>
        public string? DocumentId { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 开始处理时间
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// 完成时间
        /// </summary>
        public DateTime? CompletedAt { get; set; }
    }
}

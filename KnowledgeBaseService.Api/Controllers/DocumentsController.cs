using KnowledgeBaseService.Application.DTOs;
using KnowledgeBaseService.Application.Services;
using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Core.Entities;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeBaseService.Api.Controllers;

/// <summary>
/// 文档管理 API 控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly IRAGService _ragService;
    private readonly IFileImportService _fileImportService;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentService documentService,
        IRAGService ragService,
        IFileImportService fileImportService,
        IBackgroundTaskQueue taskQueue,
        ILogger<DocumentsController> logger)
    {
        _documentService = documentService;
        _ragService = ragService;
        _fileImportService = fileImportService;
        _taskQueue = taskQueue;
        _logger = logger;
    }

    /// <summary>
    /// 创建新文档
    /// </summary>
    [HttpPost("create")]
    [ProducesResponseType(typeof(DocumentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DocumentResponse>> CreateDocument(
        [FromBody] CreateDocumentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Creating document: {Title}", request.Title);
            
            var document = await _documentService.CreateAsync(request, cancellationToken);
            
            // 异步索引文档（不阻塞响应）
            _ = _ragService.IndexDocumentAsync(
                document.Id,
                request.Content,
                new Dictionary<string, object>
                {
                    { "title", request.Title },
                    { "category", request.Category }
                },
                cancellationToken).ConfigureAwait(false);

            return CreatedAtAction(nameof(GetDocument), new { id = document.Id }, document);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid document creation request: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating document");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to create document" });
        }
    }

    /// <summary>
    /// 外部接口：增量同步文档内容
    /// </summary>
    [HttpPost("sync-content")]
    [ProducesResponseType(typeof(UpsertDocumentContentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(UpsertDocumentContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SyncContent(
        [FromBody] UpsertDocumentContentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _documentService.UpsertContentAsync(request, cancellationToken);

            if (result.Created)
            {
                return CreatedAtAction(nameof(GetDocument), new { id = result.DocumentId }, result);
            }

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("外部同步请求参数错误: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "外部同步目标文档不存在: {DocumentId}", request.DocumentId);
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "外部同步文档失败");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "同步文档失败" });
        }
    }

    /// <summary>
    /// 获取文档
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(DocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentResponse>> GetDocument(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = await _documentService.GetAsync(id, cancellationToken);
            if (document == null)
                return NotFound(new { error = "Document not found" });

            return Ok(document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to get document" });
        }
    }

    /// <summary>
    /// 列出所有文档（分页）
    /// </summary>
    [HttpGet("list")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDocuments(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var documents = await _documentService.ListAsync(skip, take, cancellationToken);
            
            // 获取总数（需要从仓储获取）
            // 为了简单起见，这里返回分页的结果，前端可以根据实际需要调整
            var response = new
            {
                items = documents,
                total = documents.Count,
                skip = skip,
                take = take
            };
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing documents");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to list documents" });
        }
    }

    /// <summary>
    /// 删除文档
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDocument(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var success = await _documentService.DeleteAsync(id, cancellationToken);
            if (!success)
                return NotFound(new { error = "Document not found" });

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to delete document" });
        }
    }

    /// <summary>
    /// 从文件导入文档
    /// 支持格式: Word(.docx)、PDF(.pdf)、Markdown(.md)、纯文本(.txt)
    /// </summary>
    [HttpPost("import-from-file")]
    [ProducesResponseType(typeof(DocumentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<DocumentResponse>> ImportFromFile(
        [FromForm] IFormFile file,
        [FromForm] string? category = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 验证文件
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "文件不能为空" });

            var fileName = file.FileName;
            var fileSize = file.Length;

            // 检查文件大小（最大 50MB）
            const long maxFileSize = 50 * 1024 * 1024;
            if (fileSize > maxFileSize)
                return StatusCode(StatusCodes.Status413PayloadTooLarge, 
                    new { error = $"文件过大，最大支持 50MB，当前文件大小: {fileSize / (1024 * 1024)}MB" });

            // 检查文件格式是否支持
            if (!_fileImportService.IsSupportedFormat(fileName))
            {
                var supportedFormats = string.Join(", ", _fileImportService.GetSupportedExtensions());
                return BadRequest(new { 
                    error = $"不支持的文件格式。支持的格式: {supportedFormats}",
                    supportedFormats = _fileImportService.GetSupportedExtensions()
                });
            }

            _logger.LogInformation("导入文件: {FileName}, 大小: {FileSize} 字节", fileName, fileSize);

            // 提取文本内容
            string content;
            using (var stream = file.OpenReadStream())
            {
                content = await _fileImportService.ExtractTextAsync(stream, fileName, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(content))
                return BadRequest(new { error = "文件内容为空或无法解析" });

            // 创建文档
            var title = Path.GetFileNameWithoutExtension(fileName);
            // 限制标题长度为 255 字符
            if (title.Length > 255)
                title = title.Substring(0, 255);
            
            var sourceUrl = $"file://{fileName}";
            // 限制 SourceUrl 长度为 255 字符
            if (sourceUrl.Length > 255)
                sourceUrl = sourceUrl.Substring(0, 255);
            
            var fileExtension = Path.GetExtension(fileName)?.ToLowerInvariant();
            
            var createRequest = new CreateDocumentRequest
            {
                Title = title,
                Content = content,
                Category = category ?? "导入文档",
                SourceUrl = sourceUrl,
                FileExtension = fileExtension
            };

            var document = await _documentService.CreateAsync(createRequest, cancellationToken);

            // 异步索引文档
            _ = _ragService.IndexDocumentAsync(
                document.Id,
                content,
                new Dictionary<string, object>
                {
                    { "title", title },
                    { "category", createRequest.Category },
                    { "sourceFileName", fileName },
                    { "fileSize", fileSize }
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("文件导入成功，文档 ID: {DocumentId}, 内容长度: {ContentLength}", 
                document.Id, content.Length);

            return CreatedAtAction(nameof(GetDocument), new { id = document.Id }, document);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning("不支持的文件格式: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "文件处理失败");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文件导入失败");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new { error = "文件导入失败，请检查文件格式和内容" });
        }
    }

    /// <summary>
    /// 异步导入文件（推荐用于大文件和扫描版PDF）
    /// 立即返回任务ID，后台处理文件导入
    /// </summary>
    [HttpPost("import-from-file-async")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> ImportFromFileAsync(
        [FromForm] IFormFile file,
        [FromForm] string? category = null)
    {
        try
        {
            // 验证文件
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "文件不能为空" });

            var fileName = file.FileName;
            var fileSize = file.Length;

            // 检查文件大小（最大 50MB）
            const long maxFileSize = 50 * 1024 * 1024;
            if (fileSize > maxFileSize)
                return StatusCode(StatusCodes.Status413PayloadTooLarge, 
                    new { error = $"文件过大，最大支持 50MB，当前文件大小: {fileSize / (1024 * 1024)}MB" });

            // 检查文件格式是否支持
            if (!_fileImportService.IsSupportedFormat(fileName))
            {
                var supportedFormats = string.Join(", ", _fileImportService.GetSupportedExtensions());
                return BadRequest(new { 
                    error = $"不支持的文件格式。支持的格式: {supportedFormats}",
                    supportedFormats = _fileImportService.GetSupportedExtensions()
                });
            }

            // 1. 先创建文档记录（占位符）
            var title = Path.GetFileNameWithoutExtension(fileName);
            if (title.Length > 255)
                title = title.Substring(0, 255);

            var sourceUrl = $"file://{fileName}";
            if (sourceUrl.Length > 255)
                sourceUrl = sourceUrl.Substring(0, 255);

            var fileExtension = Path.GetExtension(fileName)?.ToLowerInvariant();

            var createRequest = new CreateDocumentRequest
            {
                Title = title,
                Content = "⏳ 文件正在处理中，内容即将更新...",  // 占位内容
                Category = category ?? "导入文档",
                SourceUrl = sourceUrl,
                FileExtension = fileExtension
            };

            var document = await _documentService.CreateAsync(createRequest, default);

            // 2. 保存文件到临时目录
            var tempDir = Path.Combine(Path.GetTempPath(), "knowledge_base_imports");
            Directory.CreateDirectory(tempDir);
            
            var tempFilePath = Path.Combine(tempDir, $"{Guid.NewGuid()}_{fileName}");
            
            using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            // 3. 创建后台处理任务
            var task = new DocumentImportTask
            {
                FileName = fileName,
                FileSize = fileSize,
                FilePath = tempFilePath,
                Category = category,
                DocumentId = document.Id  // 关联已创建的文档
            };

            // 4. 加入后台队列
            await _taskQueue.QueueImportTaskAsync(task);

            _logger.LogInformation("文档已创建并加入处理队列: DocumentId={DocumentId}, TaskId={TaskId}, FileName={FileName}", 
                document.Id, task.TaskId, fileName);

            return Accepted(new
            {
                taskId = task.TaskId,
                documentId = document.Id,  // ✅ 立即返回 documentId
                fileName = fileName,
                fileSize = fileSize,
                title = title,
                status = "processing",
                message = "文档已创建，文件内容正在后台处理中"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建异步导入任务失败");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new { error = "创建导入任务失败" });
        }
    }

    /// <summary>
    /// 查询导入任务状态
    /// </summary>
    [HttpGet("import-task/{taskId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetImportTaskStatus(Guid taskId)
    {
        var task = _taskQueue.GetTaskStatus(taskId);
        
        if (task == null)
        {
            return NotFound(new { error = "任务不存在" });
        }

        return Ok(new
        {
            taskId = task.TaskId,
            fileName = task.FileName,
            fileSize = task.FileSize,
            status = task.Status.ToString().ToLower(),
            progress = task.Progress,
            progressMessage = task.ProgressMessage,
            documentId = task.DocumentId,
            errorMessage = task.ErrorMessage,
            createdAt = task.CreatedAt,
            startedAt = task.StartedAt,
            completedAt = task.CompletedAt
        });
    }

    /// <summary>
    /// 批量导入文件
    /// 支持同时上传多个文件
    /// </summary>
    [HttpPost("import-files-batch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportFilesBatch(
        [FromForm] List<IFormFile> files,
        [FromForm] string? category = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (files == null || files.Count == 0)
                return BadRequest(new { error = "至少需要上传一个文件" });

            if (files.Count > 10)
                return BadRequest(new { error = "一次最多支持上传 10 个文件" });

            var results = new List<object>();
            var errors = new List<object>();

            foreach (var file in files)
            {
                try
                {
                    // 检查文件格式
                    if (!_fileImportService.IsSupportedFormat(file.FileName))
                    {
                        errors.Add(new 
                        { 
                            fileName = file.FileName, 
                            error = "不支持的文件格式" 
                        });
                        continue;
                    }

                    // 提取文本
                    string content;
                    using (var stream = file.OpenReadStream())
                    {
                        content = await _fileImportService.ExtractTextAsync(stream, file.FileName, cancellationToken);
                    }

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        errors.Add(new 
                        { 
                            fileName = file.FileName, 
                            error = "文件内容为空" 
                        });
                        continue;
                    }

                    // 创建文档
                    var title = Path.GetFileNameWithoutExtension(file.FileName);
                    // 限制标题长度为 255 字符
                    if (title.Length > 255)
                        title = title.Substring(0, 255);
                    
                    var sourceUrl = $"file://{file.FileName}";
                    // 限制 SourceUrl 长度为 255 字符
                    if (sourceUrl.Length > 255)
                        sourceUrl = sourceUrl.Substring(0, 255);
                    
                    var createRequest = new CreateDocumentRequest
                    {
                        Title = title,
                        Content = content,
                        Category = category ?? "批量导入",
                        SourceUrl = sourceUrl
                    };

                    var document = await _documentService.CreateAsync(createRequest, cancellationToken);

                    // 异步索引
                    _ = _ragService.IndexDocumentAsync(
                        document.Id,
                        content,
                        new Dictionary<string, object>
                        {
                            { "title", title },
                            { "category", createRequest.Category },
                            { "sourceFileName", file.FileName }
                        },
                        cancellationToken).ConfigureAwait(false);

                    results.Add(new
                    {
                        fileName = file.FileName,
                        documentId = document.Id,
                        title = title,
                        contentLength = content.Length,
                        status = "success"
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理文件 {FileName} 失败", file.FileName);
                    errors.Add(new 
                    { 
                        fileName = file.FileName, 
                        error = ex.Message 
                    });
                }
            }

            var response = new
            {
                totalFiles = files.Count,
                successCount = results.Count,
                failureCount = errors.Count,
                results = results,
                errors = errors.Count > 0 ? errors : null
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量文件导入失败");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new { error = "批量导入失败" });
        }
    }

    /// <summary>
    /// 获取支持的文件格式列表
    /// </summary>
    [HttpGet("supported-formats")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult GetSupportedFormats()
    {
        var extensions = _fileImportService.GetSupportedExtensions();
        
        var response = new
        {
            supported_formats = extensions,
            format_details = new
            {
                docx = new { extension = ".docx", description = "Microsoft Word 文档", supported = true },
                pdf = new { extension = ".pdf", description = "PDF 文档", supported = true },
                md = new { extension = ".md", description = "Markdown 文档", supported = true },
                txt = new { extension = ".txt", description = "纯文本文档", supported = true }
            },
            limitations = new
            {
                max_file_size = "50MB",
                max_batch_upload = 10,
                note = "文件内容将自动进行向量化并索引到 Qdrant"
            }
        };

        return Ok(response);
    }

    /// <summary>
    /// 修复历史文档的文件扩展名（从 SourceUrl 推断）
    /// </summary>
    [HttpPost("repair-extensions")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<ActionResult> RepairFileExtensions(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("开始修复历史文档的文件扩展名");
            
            // 获取所有文档（分批处理避免内存溢出）
            var allDocuments = await _documentService.ListAsync(0, 1000, cancellationToken);
            
            int repairedCount = 0;
            int skippedCount = 0;
            var errors = new List<string>();

            foreach (var doc in allDocuments)
            {
                // 跳过已有扩展名的文档
                if (!string.IsNullOrEmpty(doc.FileExtension))
                {
                    skippedCount++;
                    continue;
                }

                // 从 SourceUrl 提取扩展名
                if (!string.IsNullOrEmpty(doc.SourceUrl))
                {
                    try
                    {
                        // SourceUrl 格式: file://filename.ext
                        var fileName = doc.SourceUrl.Replace("file://", "");
                        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
                        
                        if (!string.IsNullOrEmpty(extension))
                        {
                            // 调用 Service 层更新
                            await _documentService.UpdateFileExtensionAsync(doc.Id, extension, cancellationToken);
                            repairedCount++;
                            _logger.LogInformation("已修复文档 {DocumentId} 的扩展名: {Extension}", doc.Id, extension);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"文档 {doc.Id} 修复失败: {ex.Message}");
                        _logger.LogWarning(ex, "修复文档 {DocumentId} 扩展名失败", doc.Id);
                    }
                }
            }

            var result = new
            {
                success = true,
                totalDocuments = allDocuments.Count,
                repairedCount,
                skippedCount,
                errorCount = errors.Count,
                errors = errors.Take(10).ToList() // 只返回前10个错误
            };

            _logger.LogInformation("文档扩展名修复完成: 总计 {Total}, 修复 {Repaired}, 跳过 {Skipped}, 错误 {Errors}", 
                allDocuments.Count, repairedCount, skippedCount, errors.Count);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "修复文档扩展名失败");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new { error = "修复失败", message = ex.Message });
        }
    }
}

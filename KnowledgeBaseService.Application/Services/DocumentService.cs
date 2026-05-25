using System.Text;
using KnowledgeBaseService.Application.DTOs;
using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Core.Entities;
using Microsoft.Extensions.Logging;

namespace KnowledgeBaseService.Application.Services;

/// <summary>
/// 文档管理服务实现
/// </summary>
public class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _repository;
    private readonly IDocumentVersionService _versionService;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        IDocumentRepository repository,
        IDocumentVersionService versionService,
        ILogger<DocumentService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _versionService = versionService ?? throw new ArgumentNullException(nameof(versionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 创建文档
    /// </summary>
    public async Task<DocumentResponse> CreateAsync(CreateDocumentRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("Title and Content are required");

        try
        {
            var document = new Document
            {
                Title = request.Title,
                Content = request.Content,
                Category = request.Category,
                SourceUrl = request.SourceUrl,
                FileExtension = request.FileExtension,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _repository.InsertAsync(document);

            // 自动为新文档创建初始版本
            try
            {
                await _versionService.CreateVersionAsync(
                    document.Id,
                    document.Content,
                    document.Title,
                    changeLog: "Initial import",
                    createdBy: "system",
                    tag: "initial",
                    category: document.Category,
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Created initial version for document {DocumentId}", document.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create initial version for document {DocumentId}, but document was created successfully", document.Id);
                // 不中断文档创建流程，即使版本创建失败
            }

            return new DocumentResponse
            {
                Id = document.Id,
                Title = document.Title,
                Category = document.Category,
                SourceUrl = document.SourceUrl,
                FileExtension = document.FileExtension,
                Content = document.Content,
                CreatedAt = document.CreatedAt,
                UpdatedAt = document.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating document");
            throw;
        }
    }

    /// <summary>
    /// 获取文档
    /// </summary>
    public async Task<DocumentResponse?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var doc = await _repository.GetByIdAsync(id);
        if (doc == null || doc.IsDeleted)
            return null;

        return new DocumentResponse
        {
            Id = doc.Id,
            Title = doc.Title,
            Category = doc.Category,
            SourceUrl = doc.SourceUrl,
            FileExtension = doc.FileExtension,
            Content = doc.Content,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt
        };
    }

    /// <summary>
    /// 列出所有文档
    /// </summary>
    public async Task<List<DocumentResponse>> ListAsync(int skip = 0, int take = 10, CancellationToken cancellationToken = default)
    {
        var docs = await _repository.GetListAsync(skip, take);

        return docs.Select(d => new DocumentResponse
        {
            Id = d.Id,
            Title = d.Title,
            Category = d.Category,
            SourceUrl = d.SourceUrl,
            FileExtension = d.FileExtension,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt
        }).ToList();
    }

    /// <summary>
    /// 删除文档
    /// </summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _repository.DeleteAsync(id);
    }

    /// <summary>
    /// 更新文档的文件扩展名
    /// </summary>
    public async Task<bool> UpdateFileExtensionAsync(string id, string fileExtension, CancellationToken cancellationToken = default)
    {
        try
        {
            var document = await _repository.GetByIdAsync(id);
            if (document == null || document.IsDeleted)
            {
                _logger.LogWarning("Document {DocumentId} not found or deleted", id);
                return false;
            }

            document.FileExtension = fileExtension;
            document.UpdatedAt = DateTime.UtcNow;
            
            return await _repository.UpdateAsync(document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating file extension for document {DocumentId}", id);
            throw;
        }
    }

    /// <summary>
    /// 获取内部文档对象
    /// </summary>
    public async Task<Document?> GetInternalAsync(string id, CancellationToken cancellationToken = default)
    {
        var doc = await _repository.GetByIdAsync(id);
        if (doc == null || doc.IsDeleted)
            return null;
            
        return doc;
    }

    /// <summary>
    /// 外部接口：按需创建或追加文档内容
    /// </summary>
    public async Task<UpsertDocumentContentResponse> UpsertContentAsync(UpsertDocumentContentRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("内容不能为空", nameof(request.Content));

        var normalizedTitle = string.IsNullOrWhiteSpace(request.Name) ? "未命名文档" : request.Name.Trim();
        var category = string.IsNullOrWhiteSpace(request.Category) ? "外部同步" : request.Category.Trim();
        var fileExtension = string.IsNullOrWhiteSpace(request.FileExtension) ? ".json" : request.FileExtension.Trim();

        if (string.IsNullOrWhiteSpace(request.DocumentId))
        {
            _logger.LogInformation("外部同步：创建新文档 {Title}", normalizedTitle);

            var createRequest = new CreateDocumentRequest
            {
                Title = normalizedTitle,
                Content = request.Content,
                Category = category,
                SourceUrl = request.SourceUrl,
                FileExtension = fileExtension
            };

            var document = await CreateAsync(createRequest, cancellationToken);
            var internalDoc = await _repository.GetByIdAsync(document.Id);
            var currentVersion = await _versionService.GetCurrentVersionAsync(document.Id, cancellationToken);

            return new UpsertDocumentContentResponse
            {
                DocumentId = document.Id,
                Name = document.Title,
                Created = true,
                Version = currentVersion?.VersionNumber ?? 1,
                ContentLength = internalDoc?.Content?.Length ?? request.Content.Length,
                Category = string.IsNullOrWhiteSpace(document.Category) ? category : document.Category,
                UpdatedAt = internalDoc?.UpdatedAt ?? document.UpdatedAt,
                Message = "文档已创建并同步索引"
            };
        }

        var existing = await _repository.GetByIdAsync(request.DocumentId);
        if (existing == null || existing.IsDeleted)
        {
            _logger.LogWarning("外部同步失败：文档 {DocumentId} 不存在或已删除", request.DocumentId);
            throw new KeyNotFoundException($"文档 {request.DocumentId} 不存在或已删除");
        }

        var effectiveTitle = string.IsNullOrWhiteSpace(request.Name) ? existing.Title : normalizedTitle;
        var effectiveCategory = string.IsNullOrWhiteSpace(existing.Category) ? category : existing.Category;
        var delimiter = request.AppendDelimiter ?? Environment.NewLine;

        var mergedContentBuilder = new StringBuilder(existing.Content ?? string.Empty);
        if (!string.IsNullOrEmpty(existing.Content) && !string.IsNullOrEmpty(delimiter))
        {
            mergedContentBuilder.Append(delimiter);
        }
        mergedContentBuilder.Append(request.Content);
        var mergedContent = mergedContentBuilder.ToString();

        var changeLog = string.IsNullOrWhiteSpace(request.ChangeLog)
            ? $"外部接口追加内容，新增 {request.Content.Length} 字符"
            : request.ChangeLog;
        var createdBy = string.IsNullOrWhiteSpace(request.UpdatedBy) ? "external-api" : request.UpdatedBy;
        var tag = string.IsNullOrWhiteSpace(request.Tag) ? "external" : request.Tag;

        _logger.LogInformation("外部同步：追加内容到文档 {DocumentId}，新增 {Length} 字符", existing.Id, request.Content.Length);

        var version = await _versionService.CreateVersionAsync(
            existing.Id,
            mergedContent,
            effectiveTitle,
            changeLog,
            createdBy,
            tag,
            effectiveCategory,
            cancellationToken);

        var updated = await _repository.GetByIdAsync(existing.Id) ?? existing;

        // 可选：补充文件扩展名
        if (string.IsNullOrWhiteSpace(updated.FileExtension) && !string.IsNullOrWhiteSpace(fileExtension))
        {
            updated.FileExtension = fileExtension;
            await _repository.UpdateAsync(updated);
        }

        return new UpsertDocumentContentResponse
        {
            DocumentId = updated.Id,
            Name = updated.Title,
            Created = false,
            Version = version.VersionNumber,
            ContentLength = updated.Content?.Length ?? mergedContent.Length,
            Category = updated.Category,
            UpdatedAt = updated.UpdatedAt,
            Message = "文档内容已追加并同步索引"
        };
    }
}

namespace KnowledgeBaseService.Application.Services;

/// <summary>
/// 文件导入服务接口
/// 支持多种文件格式的文本提取
/// </summary>
public interface IFileImportService
{
    /// <summary>
    /// 从 Word 文档(.docx)中提取文本
    /// </summary>
    /// <param name="fileStream">文件流</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>提取的文本内容</returns>
    Task<string> ExtractTextFromWordAsync(Stream fileStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从 PDF 文档中提取文本
    /// </summary>
    /// <param name="fileStream">文件流</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>提取的文本内容</returns>
    Task<string> ExtractTextFromPdfAsync(Stream fileStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从 Markdown 文件中提取文本
    /// </summary>
    /// <param name="fileStream">文件流</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文本内容</returns>
    Task<string> ExtractTextFromMarkdownAsync(Stream fileStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从纯文本文件中提取文本
    /// </summary>
    /// <param name="fileStream">文件流</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文本内容</returns>
    Task<string> ExtractTextFromPlainTextAsync(Stream fileStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据文件扩展名自动检测格式并提取文本
    /// </summary>
    /// <param name="fileStream">文件流</param>
    /// <param name="fileName">文件名（包含扩展名）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>提取的文本内容</returns>
    /// <exception cref="NotSupportedException">不支持的文件格式</exception>
    Task<string> ExtractTextAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证文件格式是否支持
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <returns>是否支持</returns>
    bool IsSupportedFormat(string fileName);

    /// <summary>
    /// 获取支持的文件扩展名列表
    /// </summary>
    /// <returns>扩展名列表</returns>
    IEnumerable<string> GetSupportedExtensions();
}

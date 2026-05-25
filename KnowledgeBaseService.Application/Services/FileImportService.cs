using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using KnowledgeBaseService.Application.Services;
using KnowledgeBaseService.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Diagnostics;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Path = System.IO.Path;

public class FileImportService : IFileImportService
{
    private readonly ILogger<FileImportService> _logger;
    private readonly IDoubaoVisionClient _visionClient;
    
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx",
        ".pdf",
        ".md",
        ".txt",
        ".xlsx",  // Excel 文件
        ".csv",   // CSV 文件
        ".jsonl", // JSON Lines 文件
        ".png",   // PNG 图片
        ".jpg",   // JPG 图片
        ".jpeg",  // JPEG 图片
        ".bmp",   // BMP 图片
        ".gif",   // GIF 图片
        ".pptx",  // PowerPoint 演示文稿
        ".ppt"    // PowerPoint 演示文稿（旧格式）
    };

    public FileImportService(
        ILogger<FileImportService> logger,
        IDoubaoVisionClient visionClient)
    {
        _logger = logger;
        _visionClient = visionClient;
    }

    /// <summary>
    /// 从 Word 文档(.docx)中提取文本
    /// 使用 DocumentFormat.OpenXml 库解析
    /// </summary>
    public async Task<string> ExtractTextFromWordAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("开始从 Word 文档提取文本");
                var extractedText = new StringBuilder();

                using (var document = WordprocessingDocument.Open(fileStream, false))
                {
                    if (document.MainDocumentPart?.Document?.Body == null)
                    {
                        _logger.LogWarning("Word 文档无有效内容");
                        return string.Empty;
                    }

                    var body = document.MainDocumentPart.Document.Body;

                    // 提取段落文本
                    foreach (var paragraph in body.Descendants<Paragraph>())
                    {
                        var paragraphText = ExtractParagraphText(paragraph);
                        if (!string.IsNullOrWhiteSpace(paragraphText))
                        {
                            extractedText.AppendLine(paragraphText);
                        }
                    }

                    // 提取表格内容
                    foreach (var table in body.Descendants<Table>())
                    {
                        extractedText.AppendLine(ExtractTableText(table));
                    }
                }

                _logger.LogInformation("Word 文档文本提取完成，共 {Length} 字符", extractedText.Length);
                return extractedText.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从 Word 文档提取文本失败");
                throw new InvalidOperationException("无法读取 Word 文档，请确保文件格式正确", ex);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 从 PDF 文档中提取文本（智能模式：优先文本层提取，失败则使用视觉识别）
    /// </summary>
    public async Task<string> ExtractTextFromPdfAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        // 先将流内容复制到内存，避免 using 后流被释放
        byte[] pdfBytes;
        using (var memoryStream = new MemoryStream())
        {
            await fileStream.CopyToAsync(memoryStream, cancellationToken);
            pdfBytes = memoryStream.ToArray();
        }

        return await Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("开始从 PDF 文档提取文本");
                var extractedText = new StringBuilder();
                int pageCount = 0;
                int emptyPageCount = 0;

                // 第一步：尝试文本层提取
                using (var pdfStream = new MemoryStream(pdfBytes))
                using (var pdfReader = new PdfReader(pdfStream))
                {
                    pageCount = pdfReader.NumberOfPages;
                    _logger.LogInformation("PDF 文档包含 {PageCount} 页", pageCount);

                    for (int page = 1; page <= pageCount; page++)
                    {
                        try
                        {
                            var pageText = PdfTextExtractor.GetTextFromPage(pdfReader, page);
                            
                            // 清理文本（去除多余空白）
                            pageText = pageText?.Trim() ?? string.Empty;
                            
                            if (!string.IsNullOrWhiteSpace(pageText))
                            {
                                extractedText.AppendLine($"[第 {page} 页]");
                                extractedText.AppendLine(pageText);
                                extractedText.AppendLine();
                            }
                            else
                            {
                                emptyPageCount++;
                                _logger.LogDebug("第 {PageNumber} 页未提取到文本（可能是图片或扫描页）", page);
                            }
                        }
                        catch (Exception ex)
                        {
                            emptyPageCount++;
                            _logger.LogWarning(ex, "无法从第 {PageNumber} 页提取文本", page);
                        }
                    }
                }

                var extractedLength = extractedText.Length;
                _logger.LogInformation("PDF 文本层提取完成，共 {Length} 字符（空页: {EmptyPages}/{TotalPages}）", 
                    extractedLength, emptyPageCount, pageCount);

                // 第二步：检测是否为扫描版，如果是则使用视觉识别
                if (extractedLength == 0 || (emptyPageCount > pageCount * 0.8))
                {
                    _logger.LogInformation("检测到扫描版 PDF（{EmptyPages}/{TotalPages} 页无文本层），切换到视觉识别模式", 
                        emptyPageCount, pageCount);
                    
                    // 使用新的内存流传递给视觉识别方法
                    using (var visionStream = new MemoryStream(pdfBytes))
                    {
                        return await ExtractTextFromPdfUsingVisionAsync(visionStream, pageCount, cancellationToken);
                    }
                }

                return extractedText.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从 PDF 文档提取文本失败");
                throw new InvalidOperationException("无法读取 PDF 文档，请确保文件格式正确且非加密", ex);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 使用 LibreOffice + 豆包视觉模型提取 PDF 内容（处理扫描版 PDF）
    /// </summary>
    private async Task<string> ExtractTextFromPdfUsingVisionAsync(
        Stream fileStream, 
        int pageCount,
        CancellationToken cancellationToken = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempPdfPath = Path.Combine(tempDir, "document.pdf");
        var outputDir = Path.Combine(tempDir, "images");

        try
        {
            _logger.LogInformation("开始使用视觉识别处理 PDF（{PageCount} 页）", pageCount);
            Directory.CreateDirectory(tempDir);
            
            // 保存流到临时文件
            using (var tempFileStream = File.Create(tempPdfPath))
            {
                await fileStream.CopyToAsync(tempFileStream, cancellationToken);
            }

            // 使用 LibreOffice 将 PDF 转换为图片
            var imageFiles = await ConvertPdfToImagesAsync(tempPdfPath, outputDir, cancellationToken);

            // 使用豆包视觉模型识别每张图片
            // 注意：扫描版 PDF 不拼接成一个大文本，而是按页面分割
            // 方便 RAG 检索时以页面为单位返回上下文
            var allPages = new List<string>();

            for (int i = 0; i < imageFiles.Count; i++)
            {
                _logger.LogInformation("正在识别第 {Page}/{Total} 页", i + 1, imageFiles.Count);
                
                using var imageStream = File.OpenRead(imageFiles[i]);
                var pageContent = await _visionClient.AnalyzeImageFromStreamAsync(
                    imageStream, 
                    prompt: "请逐字逐句提取图片中的所有文字内容，包括标题、正文、列表、表格等。保持原文格式和结构，不要总结或概括。如果有编号列表，请完整保留。", 
                    cancellationToken);
                
                // 打印每页识别的内容长度和前 100 字符，用于诊断
                var preview = pageContent.Length > 100 ? pageContent.Substring(0, 100) + "..." : pageContent;
                _logger.LogInformation("第 {Page} 页识别结果（{Length} 字符）: {Preview}", 
                    i + 1, pageContent.Length, preview);
                
                // 每页独立存储，包含页码信息
                var pageText = $"第 {i + 1} 页：\n{pageContent}";
                allPages.Add(pageText);
            }

            // 使用特殊分隔符拼接，便于后续按页面分块
            var separator = "\n\n---PAGE_BREAK---\n\n";
            var allContent = string.Join(separator, allPages);

            _logger.LogInformation("PDF 视觉识别完成，共 {PageCount} 页，总计 {Length} 字符", allPages.Count, allContent.Length);
            return allContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF 视觉识别失败");
            throw new InvalidOperationException($"无法使用视觉识别提取 PDF 内容: {ex.Message}", ex);
        }
        finally
        {
            // 清理临时文件
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理临时文件失败: {TempDir}", tempDir);
            }
        }
    }

    /// <summary>
    /// 使用 pdftoppm（Poppler）将 PDF 转换为图片（所有页面）
    /// </summary>
    private async Task<List<string>> ConvertPdfToImagesAsync(
        string pdfPath, 
        string outputDir,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始将 PDF 转换为图片: {PdfPath}", pdfPath);

            // 创建输出目录
            Directory.CreateDirectory(outputDir);

            // 使用 pdftoppm 转换 PDF 为 PNG 图片（所有页面）
            // -png: 输出 PNG 格式
            // -r 150: 分辨率 150 DPI（平衡质量和性能）
            // 输出文件名格式：page-001.png, page-002.png, ...
            var outputPrefix = Path.Combine(outputDir, "page");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pdftoppm",
                    Arguments = $"-png -r 150 \"{pdfPath}\" \"{outputPrefix}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogError("pdftoppm 转换 PDF 失败 (Exit Code: {ExitCode}): {Error}", process.ExitCode, error);
                throw new InvalidOperationException($"PDF 转图片失败: {error}");
            }

            // 获取生成的图片列表（按文件名排序）
            // pdftoppm 生成的文件名格式：page-1.png, page-2.png, ... 或 page-01.png, page-02.png, ...
            var imageFiles = Directory.GetFiles(outputDir, "page-*.png")
                .OrderBy(f => {
                    // 提取页码进行数字排序
                    var fileName = Path.GetFileNameWithoutExtension(f);
                    var pageNumStr = fileName.Replace("page-", "");
                    return int.TryParse(pageNumStr, out var pageNum) ? pageNum : 0;
                })
                .ToList();

            if (imageFiles.Count == 0)
            {
                throw new InvalidOperationException("PDF 转换未生成任何图片，请检查 pdftoppm 是否正确安装");
            }

            _logger.LogInformation("PDF 转换完成，生成 {Count} 张图片", imageFiles.Count);
            
            return imageFiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF 转图片失败");
            throw;
        }
    }

    /// <summary>
    /// 从纯文本文件中提取文本
    /// 支持 UTF-8 编码的 txt 文件
    /// </summary>
    public async Task<string> ExtractTextFromPlainTextAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("开始读取纯文本文件");
                
                using (var reader = new StreamReader(fileStream, Encoding.UTF8))
                {
                    var content = await reader.ReadToEndAsync();
                    
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        _logger.LogWarning("纯文本文件为空");
                        return string.Empty;
                    }

                    _logger.LogInformation("纯文本文件读取完成，共 {Length} 字符", content.Length);
                    return content;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取纯文本文件失败");
                throw new InvalidOperationException("无法读取纯文本文件", ex);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 从 Markdown 文件中提取文本
    /// Markdown 本身就是文本，直接读取即可
    /// </summary>
    public async Task<string> ExtractTextFromMarkdownAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("开始读取 Markdown 文件");
                
                using (var reader = new StreamReader(fileStream, Encoding.UTF8))
                {
                    var content = await reader.ReadToEndAsync();
                    
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        _logger.LogWarning("Markdown 文件为空");
                        return string.Empty;
                    }

                    _logger.LogInformation("Markdown 文件读取完成，共 {Length} 字符", content.Length);
                    return content;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取 Markdown 文件失败");
                throw new InvalidOperationException("无法读取 Markdown 文件", ex);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 从 Excel 文件(.xlsx)中提取文本
    /// 使用 ClosedXML 库解析，支持多个工作表
    /// </summary>
    public async Task<string> ExtractTextFromExcelAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("开始从 Excel 文档提取文本");
                var extractedText = new StringBuilder();

                using (var workbook = new XLWorkbook(fileStream))
                {
                    _logger.LogInformation("Excel 文档包含 {SheetCount} 个工作表", workbook.Worksheets.Count);

                    foreach (var worksheet in workbook.Worksheets)
                    {
                        extractedText.AppendLine($"[工作表: {worksheet.Name}]");
                        extractedText.AppendLine();

                        // 获取使用的范围（非空单元格）
                        var usedRange = worksheet.RangeUsed();
                        if (usedRange == null)
                        {
                            _logger.LogInformation("工作表 {SheetName} 为空", worksheet.Name);
                            continue;
                        }

                        // 提取表格数据
                        foreach (var row in usedRange.Rows())
                        {
                            var rowValues = new List<string>();
                            foreach (var cell in row.Cells())
                            {
                                var cellValue = cell.GetValue<string>();
                                rowValues.Add(cellValue ?? string.Empty);
                            }

                            // 跳过完全为空的行
                            if (rowValues.Any(v => !string.IsNullOrWhiteSpace(v)))
                            {
                                extractedText.AppendLine(string.Join(" | ", rowValues));
                            }
                        }

                        extractedText.AppendLine();
                    }
                }

                _logger.LogInformation("Excel 文档文本提取完成，共 {Length} 字符", extractedText.Length);
                return extractedText.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从 Excel 文档提取文本失败");
                throw new InvalidOperationException("无法读取 Excel 文档，请确保文件格式正确", ex);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 从 CSV 文件中提取文本
    /// 使用 CsvHelper 库解析，自动检测分隔符和编码
    /// </summary>
    public async Task<string> ExtractTextFromCsvAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("开始从 CSV 文件提取文本");
                var extractedText = new StringBuilder();

                using (var reader = new StreamReader(fileStream, Encoding.UTF8))
                {
                    var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true,      // 假设有表头
                        MissingFieldFound = null,    // 忽略缺失字段
                        BadDataFound = null,         // 忽略错误数据
                        TrimOptions = TrimOptions.Trim  // 自动去除空格
                    };

                    using (var csv = new CsvReader(reader, config))
                    {
                        await csv.ReadAsync();
                        csv.ReadHeader();
                        var headers = csv.HeaderRecord;

                        if (headers != null && headers.Length > 0)
                        {
                            extractedText.AppendLine($"[表头] {string.Join(" | ", headers)}");
                            extractedText.AppendLine();
                        }

                        int rowCount = 0;
                        while (await csv.ReadAsync())
                        {
                            var rowValues = new List<string>();
                            for (int i = 0; i < (headers?.Length ?? csv.Parser.Count); i++)
                            {
                                rowValues.Add(csv.GetField(i) ?? string.Empty);
                            }

                            extractedText.AppendLine(string.Join(" | ", rowValues));
                            rowCount++;
                        }

                        _logger.LogInformation("CSV 文件读取完成，共 {RowCount} 行数据", rowCount);
                    }
                }

                _logger.LogInformation("CSV 文件文本提取完成，共 {Length} 字符", extractedText.Length);
                return extractedText.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从 CSV 文件提取文本失败");
                throw new InvalidOperationException("无法读取 CSV 文件，请确保文件格式正确", ex);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 从 JSONL (JSON Lines) 文件中提取文本
    /// 每行是一个独立的 JSON 对象
    /// </summary>
    public async Task<string> ExtractTextFromJsonLinesAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("开始从 JSONL 文件提取文本");
                var extractedText = new StringBuilder();

                using (var reader = new StreamReader(fileStream, Encoding.UTF8))
                {
                    int lineNumber = 0;
                    string? line;

                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        lineNumber++;

                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        try
                        {
                            // 解析 JSON 对象
                            using var jsonDoc = JsonDocument.Parse(line);
                            var root = jsonDoc.RootElement;

                            extractedText.AppendLine($"[记录 {lineNumber}]");

                            // 递归提取所有字段值
                            ExtractJsonValues(root, extractedText, indent: 0);
                            extractedText.AppendLine();
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning(ex, "解析第 {LineNumber} 行 JSON 失败，跳过该行", lineNumber);
                            extractedText.AppendLine($"[记录 {lineNumber}] 解析失败");
                            extractedText.AppendLine();
                        }
                    }

                    _logger.LogInformation("JSONL 文件读取完成，共 {LineCount} 行记录", lineNumber);
                }

                _logger.LogInformation("JSONL 文件文本提取完成，共 {Length} 字符", extractedText.Length);
                return extractedText.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从 JSONL 文件提取文本失败");
                throw new InvalidOperationException("无法读取 JSONL 文件，请确保文件格式正确", ex);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 从图片文件中提取文本（使用豆包视觉模型）
    /// </summary>
    public async Task<string> ExtractTextFromImageAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("开始从图片文件提取文本: {FileName}", fileName);

                // 生成唯一的文档ID（用于保存图片）
                var documentId = Guid.NewGuid().ToString();
                var ext = Path.GetExtension(fileName).ToLowerInvariant();
                
                // 保存图片到文件系统
                var imageDir = Path.Combine("wwwroot", "uploads", "images");
                Directory.CreateDirectory(imageDir);
                var imagePath = Path.Combine(imageDir, $"{documentId}{ext}");
                
                // 保存图片文件
                using (var fs = new FileStream(imagePath, FileMode.Create))
                {
                    fileStream.Position = 0;
                    await fileStream.CopyToAsync(fs, cancellationToken);
                }
                
                var relativeImagePath = $"/uploads/images/{documentId}{ext}";
                _logger.LogInformation("图片已保存到: {ImagePath}", relativeImagePath);

                // 使用豆包视觉模型分析图片
                fileStream.Position = 0;
                var extractedText = await _visionClient.AnalyzeImageFromStreamAsync(
                    fileStream, 
                    prompt: null,  // 使用默认提示词
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    _logger.LogWarning("图片 {FileName} 未提取到任何内容", fileName);
                    return $"[图片文件: {fileName}]\n[图片路径: {relativeImagePath}]\n未能从图片中提取到文字内容。";
                }

                _logger.LogInformation("图片文本提取完成，共 {Length} 字符", extractedText.Length);
                
                // 添加元数据前缀，标识这是从图片提取的内容，包含图片路径
                return $"[图片文件: {fileName}]\n[图片路径: {relativeImagePath}]\n\n{extractedText}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从图片文件 {FileName} 提取文本失败", fileName);
                throw new InvalidOperationException($"无法从图片文件提取内容: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 从 PPT 文件提取内容（使用 LibreOffice + 豆包视觉识别）
    /// </summary>
    public async Task<string> ExtractTextFromPptAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempPptPath = Path.Combine(tempDir, fileName);
        var outputDir = Path.Combine(tempDir, "images");

        try
        {
            _logger.LogInformation("开始处理 PPT 文件: {FileName}", fileName);
            Directory.CreateDirectory(tempDir);
            
            // 保存流到临时文件
            using (var tempFileStream = File.Create(tempPptPath))
            {
                await fileStream.CopyToAsync(tempFileStream, cancellationToken);
            }

            // 使用 LibreOffice 将 PPT 转换为图片
            var imageFiles = await ConvertPptToImagesAsync(tempPptPath, outputDir, cancellationToken);

            // 使用豆包视觉模型识别每张图片
            var allContent = new StringBuilder();
            allContent.AppendLine($"[PPT 文件: {fileName}]");
            allContent.AppendLine($"共 {imageFiles.Count} 页\n");

            for (int i = 0; i < imageFiles.Count; i++)
            {
                _logger.LogInformation("正在识别第 {Page}/{Total} 页", i + 1, imageFiles.Count);
                
                allContent.AppendLine($"=== 第 {i + 1} 页 ===");
                
                using var imageStream = File.OpenRead(imageFiles[i]);
                var pageContent = await _visionClient.AnalyzeImageFromStreamAsync(
                    imageStream, 
                    prompt: "提取这页 PPT 的所有文字内容、图表数据和关键信息。用简洁的语言描述，便于检索。", 
                    cancellationToken);
                
                allContent.AppendLine(pageContent);
                allContent.AppendLine();
            }

            _logger.LogInformation("PPT 内容提取完成，共 {Length} 字符", allContent.Length);
            return allContent.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从 PPT 文件 {FileName} 提取内容失败", fileName);
            throw new InvalidOperationException($"无法从 PPT 文件提取内容: {ex.Message}", ex);
        }
        finally
        {
            // 清理临时文件
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理临时文件失败: {TempDir}", tempDir);
            }
        }
    }

    /// <summary>
    /// 使用 LibreOffice + pdftoppm 将 PPT 转换为图片（所有页面）
    /// 策略：PPT → PDF → PNG（避免 LibreOffice 直接转 PNG 只生成第一页的问题）
    /// </summary>
    private async Task<List<string>> ConvertPptToImagesAsync(
        string pptPath, 
        string outputDir,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始将 PPT 转换为图片: {PptPath}", pptPath);

            // 创建输出目录
            Directory.CreateDirectory(outputDir);

            // 第一步：PPT 转 PDF（LibreOffice 这个功能是正常的）
            var pdfPath = Path.Combine(outputDir, "temp.pdf");
            var pptToPdfProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "soffice",
                    Arguments = $"--headless --convert-to pdf --outdir \"{outputDir}\" \"{pptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            pptToPdfProcess.Start();
            
            var output1 = await pptToPdfProcess.StandardOutput.ReadToEndAsync(cancellationToken);
            var error1 = await pptToPdfProcess.StandardError.ReadToEndAsync(cancellationToken);
            
            await pptToPdfProcess.WaitForExitAsync(cancellationToken);

            if (pptToPdfProcess.ExitCode != 0)
            {
                _logger.LogError("LibreOffice PPT 转 PDF 失败 (Exit Code: {ExitCode}): {Error}", 
                    pptToPdfProcess.ExitCode, error1);
                throw new InvalidOperationException($"PPT 转 PDF 失败: {error1}");
            }

            // LibreOffice 输出的 PDF 文件名可能是原文件名.pdf
            var generatedPdfFiles = Directory.GetFiles(outputDir, "*.pdf");
            if (generatedPdfFiles.Length == 0)
            {
                throw new InvalidOperationException("PPT 转 PDF 未生成任何文件");
            }
            
            var actualPdfPath = generatedPdfFiles[0];
            _logger.LogInformation("PPT 已转换为 PDF: {PdfPath}", actualPdfPath);

            // 第二步：PDF 转 PNG（使用 pdftoppm，支持所有页面）
            var outputPrefix = Path.Combine(outputDir, "slide");
            var pdfToPngProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pdftoppm",
                    Arguments = $"-png -r 150 \"{actualPdfPath}\" \"{outputPrefix}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            pdfToPngProcess.Start();
            
            var output2 = await pdfToPngProcess.StandardOutput.ReadToEndAsync(cancellationToken);
            var error2 = await pdfToPngProcess.StandardError.ReadToEndAsync(cancellationToken);
            
            await pdfToPngProcess.WaitForExitAsync(cancellationToken);

            if (pdfToPngProcess.ExitCode != 0)
            {
                _logger.LogError("pdftoppm 转换失败 (Exit Code: {ExitCode}): {Error}", 
                    pdfToPngProcess.ExitCode, error2);
                throw new InvalidOperationException($"PDF 转图片失败: {error2}");
            }

            // 获取生成的图片列表（按页码数字排序）
            var imageFiles = Directory.GetFiles(outputDir, "slide-*.png")
                .OrderBy(f => {
                    var fileName = Path.GetFileNameWithoutExtension(f);
                    var pageNumStr = fileName.Replace("slide-", "");
                    return int.TryParse(pageNumStr, out var pageNum) ? pageNum : 0;
                })
                .ToList();

            if (imageFiles.Count == 0)
            {
                throw new InvalidOperationException("PPT 转换未生成任何图片");
            }

            _logger.LogInformation("PPT 转换完成，生成 {Count} 张图片", imageFiles.Count);
            
            return imageFiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PPT 转图片失败");
            throw;
        }
    }

    /// <summary>
    /// 根据文件扩展名自动检测格式并提取文本
    /// </summary>
    public async Task<string> ExtractTextAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("文件名不能为空", nameof(fileName));

        var extension = Path.GetExtension(fileName).ToLower();

        return extension switch
        {
            ".docx" => await ExtractTextFromWordAsync(fileStream, cancellationToken),
            ".pdf" => await ExtractTextFromPdfAsync(fileStream, cancellationToken),
            ".md" => await ExtractTextFromMarkdownAsync(fileStream, cancellationToken),
            ".txt" => await ExtractTextFromPlainTextAsync(fileStream, cancellationToken),
            ".xlsx" => await ExtractTextFromExcelAsync(fileStream, cancellationToken),
            ".csv" => await ExtractTextFromCsvAsync(fileStream, cancellationToken),
            ".jsonl" => await ExtractTextFromJsonLinesAsync(fileStream, cancellationToken),
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" => await ExtractTextFromImageAsync(fileStream, fileName, cancellationToken),
            ".pptx" or ".ppt" => await ExtractTextFromPptAsync(fileStream, fileName, cancellationToken),
            _ => throw new NotSupportedException($"不支持的文件格式: {extension}，支持的格式: {string.Join(", ", SupportedExtensions)}")
        };
    }

    /// <summary>
    /// 验证文件格式是否支持
    /// </summary>
    public bool IsSupportedFormat(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var extension = Path.GetExtension(fileName).ToLower();
        return SupportedExtensions.Contains(extension);
    }

    /// <summary>
    /// 获取支持的文件扩展名列表
    /// </summary>
    public IEnumerable<string> GetSupportedExtensions()
    {
        return SupportedExtensions.ToList();
    }

    /// <summary>
    /// 提取段落文本的辅助方法
    /// </summary>
    private static string ExtractParagraphText(Paragraph paragraph)
    {
        var textBuilder = new StringBuilder();

        // 遍历段落中的所有 Run（文本运行）
        foreach (var run in paragraph.Descendants<Run>())
        {
            // 在每个 Run 中查找 Text 元素
            foreach (var text in run.Descendants<Text>())
            {
                textBuilder.Append(text.Text);
            }
        }

        return textBuilder.ToString();
    }

    /// <summary>
    /// 提取表格文本的辅助方法
    /// </summary>
    private static string ExtractTableText(Table table)
    {
        var tableBuilder = new StringBuilder();
        tableBuilder.AppendLine("[表格开始]");

        foreach (var row in table.Descendants<TableRow>())
        {
            var rowCells = new List<string>();

            foreach (var cell in row.Descendants<TableCell>())
            {
                var cellText = new StringBuilder();
                foreach (var paragraph in cell.Descendants<Paragraph>())
                {
                    foreach (var text in paragraph.Descendants<Text>())
                    {
                        cellText.Append(text.Text);
                    }
                }
                rowCells.Add(cellText.ToString());
            }

            tableBuilder.AppendLine(string.Join(" | ", rowCells));
        }

        tableBuilder.AppendLine("[表格结束]");
        return tableBuilder.ToString();
    }

    /// <summary>
    /// 递归提取 JSON 元素的值
    /// </summary>
    private static void ExtractJsonValues(JsonElement element, StringBuilder output, int indent)
    {
        string indentStr = new string(' ', indent * 2);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    output.Append($"{indentStr}{property.Name}: ");
                    
                    if (property.Value.ValueKind == JsonValueKind.Object || 
                        property.Value.ValueKind == JsonValueKind.Array)
                    {
                        output.AppendLine();
                        ExtractJsonValues(property.Value, output, indent + 1);
                    }
                    else
                    {
                        ExtractJsonValues(property.Value, output, 0);
                    }
                }
                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    output.Append($"{indentStr}[{index}]: ");
                    
                    if (item.ValueKind == JsonValueKind.Object || 
                        item.ValueKind == JsonValueKind.Array)
                    {
                        output.AppendLine();
                        ExtractJsonValues(item, output, indent + 1);
                    }
                    else
                    {
                        ExtractJsonValues(item, output, 0);
                    }
                    
                    index++;
                }
                break;

            case JsonValueKind.String:
                output.AppendLine(element.GetString());
                break;

            case JsonValueKind.Number:
                output.AppendLine(element.GetRawText());
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                output.AppendLine(element.GetBoolean().ToString());
                break;

            case JsonValueKind.Null:
                output.AppendLine("null");
                break;

            default:
                output.AppendLine(element.GetRawText());
                break;
        }
    }
}

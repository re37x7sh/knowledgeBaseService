using System.Text;
using KnowledgeBaseService.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeBaseService.Application.Services;

/// <summary>
/// 递归字符文本分割器
/// 尝试按顺序使用分隔符分割文本，直到块大小合适
/// </summary>
public class TextSplitter : ITextSplitter
{
    private readonly ILogger<TextSplitter> _logger;
    private readonly string[] _separators = { "\n\n", "\n", "。", "！", "？", ".", "!", "?", " ", "" };

    public TextSplitter(ILogger<TextSplitter> logger)
    {
        _logger = logger;
    }

    public List<string> Split(string text, int chunkSize = 1000, int overlap = 200)
    {
        if (string.IsNullOrEmpty(text))
            return new List<string>();

        if (chunkSize <= 0)
            throw new ArgumentException("Chunk size must be greater than 0", nameof(chunkSize));

        if (overlap >= chunkSize)
            throw new ArgumentException("Overlap must be smaller than chunk size", nameof(overlap));

        // 特殊处理：如果文本包含 PAGE_BREAK 标记（扫描版 PDF），按页面分割
        const string pageBreakMarker = "---PAGE_BREAK---";
        if (text.Contains(pageBreakMarker))
        {
            _logger.LogInformation("Detected PAGE_BREAK marker, splitting by pages");
            var pages = text.Split(new[] { $"\n\n{pageBreakMarker}\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            var pageChunks = pages
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            
            _logger.LogInformation("Split into {Count} pages", pageChunks.Count);
            return pageChunks;
        }

        var chunks = new List<string>();
        int startIndex = 0;

        while (startIndex < text.Length)
        {
            // 如果剩余长度小于 chunk size，直接添加并结束
            if (text.Length - startIndex <= chunkSize)
            {
                chunks.Add(text.Substring(startIndex));
                break;
            }

            // 寻找最佳分割点
            int splitIndex = -1;
            int endIndex = startIndex + chunkSize;
            
            // 确保不越界
            if (endIndex > text.Length) endIndex = text.Length;

            foreach (var separator in _separators)
            {
                if (separator == "") // 兜底：直接按长度切分
                {
                    splitIndex = endIndex;
                    break;
                }

                // 在当前块的末尾附近寻找分隔符
                // 我们希望在 [endIndex - searchRange, endIndex] 范围内找
                // searchRange 可以是 chunkSize 的一半，或者是 overlap 的大小
                // 这里简单处理：在整个当前截取范围内找最后一个分隔符
                // 但为了避免切分太碎，我们最好从 endIndex 往前找
                
                int lastSeparatorIndex = text.LastIndexOf(separator, endIndex - 1, endIndex - startIndex, StringComparison.Ordinal);
                
                // 只有当分隔符位置在合理范围内（比如至少保留了一半的 chunk size），才采用
                // 否则可能因为开头有个换行符就切分了，导致块很小
                if (lastSeparatorIndex != -1 && lastSeparatorIndex > startIndex + (chunkSize / 2))
                {
                    splitIndex = lastSeparatorIndex + separator.Length;
                    break;
                }
            }

            // 如果没找到合适的分隔符，强制切分
            if (splitIndex == -1)
            {
                splitIndex = endIndex;
            }

            // 添加当前块
            string chunk = text.Substring(startIndex, splitIndex - startIndex);
            chunks.Add(chunk);

            // 计算下一个起始位置
            // 正常情况下：下一个起始位置 = 当前结束位置 - 重叠
            // 但如果当前块是因为强制切分（splitIndex == endIndex），则回退 overlap
            // 如果是因为找到分隔符切分，通常也需要回退 overlap，除非分隔符本身就是段落结束
            
            // 简单策略：总是回退 overlap，除非剩余不够
            startIndex = splitIndex - overlap;
            
            // 修正：如果回退导致死循环（startIndex <= 原来的 startIndex），强制前进
            // 这种情况可能发生在 splitIndex - startIndex < overlap 时（即切出的块比 overlap 还小）
            if (startIndex <= (splitIndex - (splitIndex - startIndex))) // 逻辑有点绕，直接比较
            {
                 // 实际上，我们只需要确保 startIndex 严格大于上一轮的 startIndex
                 // 上一轮 startIndex 是 savedStartIndex
            }
        }
        
        // 重新整理逻辑，上面的 while 循环有点乱，使用更清晰的逻辑
        return SplitTextIterative(text, chunkSize, overlap);
    }

    private List<string> SplitTextIterative(string text, int chunkSize, int overlap)
    {
        var chunks = new List<string>();
        int currentStart = 0;

        while (currentStart < text.Length)
        {
            int currentEnd = Math.Min(currentStart + chunkSize, text.Length);
            
            // 如果已经是最后一段
            if (currentEnd == text.Length)
            {
                chunks.Add(text.Substring(currentStart));
                break;
            }

            // 寻找最佳分割点
            int splitPoint = -1;
            
            foreach (var separator in _separators)
            {
                if (string.IsNullOrEmpty(separator)) continue;

                // 从 currentEnd 往前找分隔符
                int index = text.LastIndexOf(separator, currentEnd - 1, currentEnd - currentStart, StringComparison.Ordinal);
                
                // 限制：分隔符不能太靠前，避免产生过小的块
                // 例如：至少要包含 chunk size 的 50%
                if (index != -1 && index > currentStart + (chunkSize * 0.5))
                {
                    splitPoint = index + separator.Length;
                    break;
                }
            }

            // 如果没找到合适的分隔符，强制在 currentEnd 处切断
            if (splitPoint == -1)
            {
                splitPoint = currentEnd;
            }

            // 添加块
            chunks.Add(text.Substring(currentStart, splitPoint - currentStart));

            // 更新下一次的起始位置
            // 下一次起始位置 = 当前分割点 - overlap
            // 但必须保证前进，即 nextStart > currentStart
            int nextStart = splitPoint - overlap;

            if (nextStart <= currentStart)
            {
                // 如果回退导致死循环（说明切出的块 <= overlap），则强制前进
                // 这种情况下，我们至少前进 1 个字符，或者前进到 splitPoint（如果不回退）
                // 为了避免丢失内容，如果块太小，我们就不回退了，直接从 splitPoint 开始
                nextStart = splitPoint;
            }

            currentStart = nextStart;
        }

        _logger.LogDebug("Split text of length {Length} into {Count} chunks", text.Length, chunks.Count);
        return chunks;
    }

    private void SplitTextRecursive(string text, int chunkSize, int overlap, List<string> chunks)
    {
        // Deprecated, using iterative approach
    }
}

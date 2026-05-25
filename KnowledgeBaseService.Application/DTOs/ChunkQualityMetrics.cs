namespace KnowledgeBaseService.Application.DTOs;

/// <summary>
/// 分块质量指标
/// 用于监控和优化语义分块效果
/// </summary>
public class ChunkQualityMetrics
{
    /// <summary>
    /// 总块数
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// 平均块大小（字符数）
    /// </summary>
    public double AverageChunkSize { get; set; }

    /// <summary>
    /// 块大小标准差
    /// 反映块大小的分布均匀性，越小越均匀
    /// </summary>
    public double StdDevChunkSize { get; set; }

    /// <summary>
    /// 最小块大小
    /// </summary>
    public int MinChunkSize { get; set; }

    /// <summary>
    /// 最大块大小
    /// </summary>
    public int MaxChunkSize { get; set; }

    /// <summary>
    /// 平均相似度（分割点处的相似度）
    /// </summary>
    public double AverageSimilarity { get; set; }

    /// <summary>
    /// 相似度标准差
    /// </summary>
    public double StdDevSimilarity { get; set; }

    /// <summary>
    /// 低相似度分割点数量（相似度 < 0.5）
    /// 反映语义边界检测的准确性
    /// </summary>
    public int LowSimilaritySplits { get; set; }

    /// <summary>
    /// 单句子块数量
    /// 应该尽量少（理想情况为 0）
    /// </summary>
    public int SingleSentenceChunks { get; set; }

    /// <summary>
    /// 双句子块数量
    /// </summary>
    public int TwoSentenceChunks { get; set; }

    /// <summary>
    /// 碎片块数量（1-2个句子的块）
    /// </summary>
    public int FragmentedChunks => SingleSentenceChunks + TwoSentenceChunks;

    /// <summary>
    /// 缓存命中率（%）
    /// </summary>
    public double CacheHitRate { get; set; }

    /// <summary>
    /// 总处理时间（毫秒）
    /// </summary>
    public long TotalProcessingTimeMs { get; set; }

    /// <summary>
    /// Embedding 生成时间（毫秒）
    /// </summary>
    public long EmbeddingTimeMs { get; set; }

    /// <summary>
    /// 分块合并时间（毫秒）
    /// </summary>
    public long MergingTimeMs { get; set; }

    /// <summary>
    /// 重试次数
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// 从缓存获取的句子数量
    /// </summary>
    public int CacheHits { get; set; }

    /// <summary>
    /// 总句子数量
    /// </summary>
    public int TotalSentences { get; set; }

    /// <summary>
    /// 生成质量报告
    /// </summary>
    public string GenerateReport()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("=== 语义分块质量报告 ===");
        report.AppendLine();
        report.AppendLine($"总块数: {TotalChunks}");
        report.AppendLine($"块大小: 平均 {AverageChunkSize:F0} 字符, 最小 {MinChunkSize}, 最大 {MaxChunkSize}");
        report.AppendLine($"块大小标准差: {StdDevChunkSize:F2} (越小越均匀)");
        report.AppendLine();
        report.AppendLine($"分割点平均相似度: {AverageSimilarity:P2}");
        report.AppendLine($"低相似度分割点: {LowSimilaritySplits} ({(TotalChunks > 0 ? (double)LowSimilaritySplits / TotalChunks : 0):P2})");
        report.AppendLine();
        report.AppendLine($"碎片块 (1-2句): {FragmentedChunks} ({(TotalChunks > 0 ? (double)FragmentedChunks / TotalChunks : 0):P2})");
        report.AppendLine($"  - 单句子块: {SingleSentenceChunks}");
        report.AppendLine($"  - 双句子块: {TwoSentenceChunks}");
        report.AppendLine();
        report.AppendLine($"缓存命中率: {CacheHitRate:P2} ({CacheHits}/{TotalSentences})");
        report.AppendLine($"处理时间: 总计 {TotalProcessingTimeMs}ms (Embedding: {EmbeddingTimeMs}ms, 合并: {MergingTimeMs}ms)");
        report.AppendLine($"重试次数: {RetryCount}");

        return report.ToString();
    }
}

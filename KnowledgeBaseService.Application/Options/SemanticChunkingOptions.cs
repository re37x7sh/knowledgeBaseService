namespace KnowledgeBaseService.Application.Options;

/// <summary>
/// 语义分块配置选项
/// </summary>
public class SemanticChunkingOptions
{
    /// <summary>
    /// 相似度阈值（默认 0.65）
    /// 低于此值时认为语义发生变化，应该断开
    /// </summary>
    public double SimilarityThreshold { get; set; } = 0.65;

    /// <summary>
    /// 最大块大小（字符数，默认 1500）
    /// </summary>
    public int MaxChunkSize { get; set; } = 1500;

    /// <summary>
    /// 最小块大小（字符数，默认 100）
    /// 避免产生过小的碎片块
    /// </summary>
    public int MinChunkSize { get; set; } = 100;

    /// <summary>
    /// 最少句子数（默认 3）
    /// 避免单个块只有1-2个句子
    /// </summary>
    public int MinSentencesPerChunk { get; set; } = 3;

    /// <summary>
    /// 最多句子数（默认 15）
    /// 防止单个块包含过多句子
    /// </summary>
    public int MaxSentencesPerChunk { get; set; } = 15;

    /// <summary>
    /// 滑动窗口大小（默认 3）
    /// 计算相似度时考虑的上下文句子数量
    /// </summary>
    public int WindowSize { get; set; } = 3;

    /// <summary>
    /// 重叠比例（默认 0.15，即15%）
    /// 块之间的重叠部分比例
    /// </summary>
    public float OverlapRatio { get; set; } = 0.15f;

    /// <summary>
    /// 重叠句子数（默认 2）
    /// 块之间至少保留的句子数量
    /// </summary>
    public int OverlapSentences { get; set; } = 2;

    /// <summary>
    /// 是否启用缓存（默认 true）
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// 缓存过期时间（天，默认 7）
    /// </summary>
    public int CacheTTLDays { get; set; } = 1;

    /// <summary>
    /// 批量处理最大句子数（默认 50）
    /// 避免单次 API 调用超时
    /// </summary>
    public int MaxBatchSize { get; set; } = 50;

    /// <summary>
    /// 最大重试次数（默认 3）
    /// Embedding API 调用失败时的重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 是否启用段落感知分割（默认 true）
    /// 先按段落分割，再在段落内部进行语义分块
    /// </summary>
    public bool EnableParagraphAware { get; set; } = true;

    /// <summary>
    /// 是否启用并行计算（默认 true）
    /// 并行计算相似度以提高性能
    /// </summary>
    public bool EnableParallelProcessing { get; set; } = true;
}

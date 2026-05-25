using KnowledgeBaseService.Application.DTOs;
using KnowledgeBaseService.Application.Options;
using KnowledgeBaseService.Core.Entities;

namespace KnowledgeBaseService.Application.Interfaces;

/// <summary>
/// 语义文本分割器接口
/// 基于句子边界和语义相似度进行智能分块
/// </summary>
public interface ISemanticTextSplitter
{
    /// <summary>
    /// 异步分割文本（基于语义边界）- 简化版本
    /// </summary>
    /// <param name="text">原始文本</param>
    /// <param name="similarityThreshold">相似度阈值（低于此值则断开），默认 0.65</param>
    /// <param name="maxChunkSize">最大块大小（字符数），默认 1500</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>语义相关的文本片段列表</returns>
    Task<List<string>> SplitAsync(
        string text,
        double similarityThreshold = 0.65,
        int maxChunkSize = 1500,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步分割文本（基于语义边界）- 带质量指标版本
    /// </summary>
    /// <param name="text">原始文本</param>
    /// <param name="options">分块配置选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分块结果和质量指标</returns>
    Task<(List<string> Chunks, ChunkQualityMetrics Metrics)> SplitWithMetricsAsync(
        string text,
        SemanticChunkingOptions? options = null,
        CancellationToken cancellationToken = default);
}

namespace KnowledgeBaseService.Application.Interfaces;

/// <summary>
/// 文本分割器接口
/// 用于将长文本分割成较小的片段（Chunks）
/// </summary>
public interface ITextSplitter
{
    /// <summary>
    /// 分割文本
    /// </summary>
    /// <param name="text">原始文本</param>
    /// <param name="chunkSize">分块大小（字符数）</param>
    /// <param name="overlap">重叠大小（字符数）</param>
    /// <returns>文本片段列表</returns>
    List<string> Split(string text, int chunkSize = 1000, int overlap = 200);
}

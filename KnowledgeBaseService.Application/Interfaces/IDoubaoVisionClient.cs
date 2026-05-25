namespace KnowledgeBaseService.Application.Interfaces;

/// <summary>
/// 豆包视觉模型客户端接口
/// 用于图片内容识别和描述
/// </summary>
public interface IDoubaoVisionClient
{
    /// <summary>
    /// 分析图片并提取文字描述
    /// </summary>
    /// <param name="imageBase64">图片的 Base64 编码</param>
    /// <param name="prompt">可选的提示词，指导模型如何分析图片</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>图片的文字描述</returns>
    Task<string> AnalyzeImageAsync(
        string imageBase64, 
        string? prompt = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 从图片流分析并提取文字描述
    /// </summary>
    /// <param name="imageStream">图片流</param>
    /// <param name="prompt">可选的提示词</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>图片的文字描述</returns>
    Task<string> AnalyzeImageFromStreamAsync(
        Stream imageStream, 
        string? prompt = null, 
        CancellationToken cancellationToken = default);
}

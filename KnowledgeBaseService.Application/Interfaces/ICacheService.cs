namespace KnowledgeBaseService.Application.Interfaces;

/// <summary>
/// 缓存服务接口
/// 用于缓存 Embedding 向量等数据
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// 获取缓存值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>缓存的值，如果不存在则返回 null</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 设置缓存值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">缓存键</param>
    /// <param name="value">要缓存的值</param>
    /// <param name="expiry">过期时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功设置</returns>
    Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 删除缓存值
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功删除</returns>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量获取缓存值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="keys">缓存键集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>键值对字典（仅包含存在的键）</returns>
    Task<Dictionary<string, T>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 批量设置缓存值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="items">键值对集合</param>
    /// <param name="expiry">过期时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功设置的数量</returns>
    Task<int> SetManyAsync<T>(Dictionary<string, T> items, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 检查缓存是否存在
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否存在</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空所有缓存
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    Task<bool> FlushAsync(CancellationToken cancellationToken = default);
}

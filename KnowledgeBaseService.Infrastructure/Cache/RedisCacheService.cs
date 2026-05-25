using System.Text.Json;
using KnowledgeBaseService.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace KnowledgeBaseService.Infrastructure.Cache;

/// <summary>
/// Redis 缓存服务实现
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly bool _enabled;

    public RedisCacheService(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<RedisCacheService> logger)
    {
        _enabled = configuration.GetValue<bool>("Redis:Enabled", true);
        if (!_enabled)
        {
            _logger.LogWarning("Redis 缓存已禁用");
            return;
        }

        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _database = _redis.GetDatabase();
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        if (!_enabled) return null;

        try
        {
            var value = await _database.StringGetAsync(key);
            if (value.IsNullOrEmpty) return null;

            var result = JsonSerializer.Deserialize<T>(value!);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从 Redis 获取缓存失败: {Key}", key);
            return null;
        }
    }

    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
    {
        if (!_enabled) return false;

        try
        {
            var json = JsonSerializer.Serialize(value);
            return await _database.StringSetAsync(key, json, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置 Redis 缓存失败: {Key}", key);
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return false;

        try
        {
            return await _database.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除 Redis 缓存失败: {Key}", key);
            return false;
        }
    }

    public async Task<Dictionary<string, T>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class
    {
        var result = new Dictionary<string, T>();

        if (!_enabled) return result;

        try
        {
            var keyArray = keys.ToArray();
            var values = await _database.StringGetAsync(keyArray.Select(k => (RedisKey)k).ToArray());

            for (int i = 0; i < keyArray.Length; i++)
            {
                if (!values[i].IsNull)
                {
                    var deserialized = JsonSerializer.Deserialize<T>(values[i]!);
                    if (deserialized != null)
                    {
                        result[keyArray[i]] = deserialized;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量从 Redis 获取缓存失败");
        }

        return result;
    }

    public async Task<int> SetManyAsync<T>(Dictionary<string, T> items, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class
    {
        if (!_enabled) return 0;

        int successCount = 0;

        try
        {
            var tasks = items.Select(async kvp =>
            {
                var json = JsonSerializer.Serialize(kvp.Value);
                return await _database.StringSetAsync(kvp.Key, json, expiry);
            });

            var results = await Task.WhenAll(tasks);
            successCount = results.Count(r => r);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量设置 Redis 缓存失败");
        }

        return successCount;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_enabled) return false;

        try
        {
            return await _database.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查 Redis 缓存存在性失败: {Key}", key);
            return false;
        }
    }

    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled) return false;

        try
        {
            var endpoints = _redis.GetEndPoints();
            foreach (var endpoint in endpoints)
            {
                var server = _redis.GetServer(endpoint);
                await server.FlushDatabaseAsync();
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清空 Redis 缓存失败");
            return false;
        }
    }
}

using System.Text;
using System.Text.Json;
using KnowledgeBaseService.Core.Constants;
using KnowledgeBaseService.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KnowledgeBaseService.Infrastructure.Clients;

/// <summary>
/// Qdrant HTTP 客户端实现
/// </summary>
public class QdrantHttpClient : IQdrantHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly ILogger<QdrantHttpClient> _logger;

    public QdrantHttpClient(HttpClient httpClient, IConfiguration configuration, ILogger<QdrantHttpClient> logger)
    {
        _httpClient = httpClient;
        _baseUrl = (configuration["Qdrant:BaseUrl"] ?? "http://localhost:6333").TrimEnd('/');
        _logger = logger;
    }

    /// <summary>
    /// 初始化集合
    /// </summary>
    public async Task InitializeCollectionAsync(string collectionName, int vectorDimension, CancellationToken cancellationToken = default)
    {
        try
        {
            // 检查集合是否存在
            var infoUrl = $"{_baseUrl}/collections/{collectionName}";
            var headRequest = new HttpRequestMessage(HttpMethod.Head, infoUrl);
            var headResponse = await _httpClient.SendAsync(headRequest, cancellationToken);

            if (headResponse.IsSuccessStatusCode)
            {
                // 集合已存在，检查维度是否匹配
                var getUrl = $"{_baseUrl}/collections/{collectionName}";
                var getResponse = await _httpClient.GetAsync(getUrl, cancellationToken);
                if (getResponse.IsSuccessStatusCode)
                {
                    var responseContent = await getResponse.Content.ReadAsStringAsync(cancellationToken);
                    var jsonResponse = JsonDocument.Parse(responseContent);
                    
                    if (jsonResponse.RootElement.TryGetProperty("result", out var result) &&
                        result.TryGetProperty("config", out var config) &&
                        config.TryGetProperty("params", out var @params) &&
                        @params.TryGetProperty("vectors", out var vectors) &&
                        vectors.TryGetProperty("size", out var sizeElement))
                    {
                        var currentSize = sizeElement.GetInt32();
                        if (currentSize == vectorDimension)
                            return; // 维度匹配，直接返回
                        
                        // 维度不匹配，删除旧集合
                        var deleteUrl = $"{_baseUrl}/collections/{collectionName}";
                        await _httpClient.DeleteAsync(deleteUrl, cancellationToken);
                    }
                }
            }

            // 创建集合（配置向量索引和 BM25 文本索引）
            var createUrl = $"{_baseUrl}/collections/{collectionName}";
            var createRequest = new
            {
                vectors = new
                {
                    size = vectorDimension,
                    distance = "Cosine"
                },
                // 配置 payload 索引以支持 BM25 和高效过滤
                optimizers_config = new
                {
                    indexing_threshold = 20000
                },
                // 配置 content 字段为 full-text 索引，支持 BM25 搜索
                payload_schema = new
                {
                    content = new
                    {
                        type = "text"
                    },
                    document_id = new
                    {
                        type = "keyword"
                    }
                }
            };

            var jsonContent = JsonSerializer.Serialize(createRequest);
            var httpRequest = new HttpRequestMessage(HttpMethod.Put, createUrl)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

            // 409 Conflict 表示集合已存在，尝试更新 Payload Schema
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogInformation("集合 {Collection} 已存在，检查并更新 Payload Schema", collectionName);
                await UpdatePayloadSchemaAsync(collectionName, cancellationToken);
                return;
            }

            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to initialize Qdrant collection: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 上传点（向量 + 元数据）
    /// </summary>
    public async Task<bool> UpsertPointAsync(string collectionName, ulong pointId, float[] vector, Dictionary<string, object> payload, CancellationToken cancellationToken = default)
    {
        try
        {
            // 清理 payload：将非基本类型转换为字符串
            var cleanedPayload = new Dictionary<string, object>();
            foreach (var (key, value) in payload)
            {
                if (value == null)
                    cleanedPayload[key] = "";
                else if (value is string || value is int || value is long || value is float || value is double || value is bool)
                    cleanedPayload[key] = value;
                else
                    // 复杂对象序列化为 JSON 字符串
                    cleanedPayload[key] = JsonSerializer.Serialize(value);
            }

            var url = $"{_baseUrl}/collections/{collectionName}/points?wait=true";
            var request = new
            {
                points = new[]
                {
                    new
                    {
                        id = pointId,
                        vector = vector,
                        payload = cleanedPayload
                    }
                }
            };

            var jsonContent = JsonSerializer.Serialize(request);
            var httpRequest = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to upsert point in Qdrant: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 搜索相似向量
    /// </summary>
    public async Task<List<(ulong PointId, float Score, Dictionary<string, object> Payload)>> SearchAsync(
        string collectionName, 
        float[] vector, 
        int topK = 5, 
        float scoreThreshold = 0.5f, 
        List<string>? documentIds = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(ulong, float, Dictionary<string, object>)>();

        try
        {
            topK = Math.Min(topK, QdrantConstants.MaxTopK);
            var url = $"{_baseUrl}/collections/{collectionName}/points/search";

            // 构建 filter（如果有 documentIds）
            object? filter = null;
            if (documentIds != null && documentIds.Count > 0)
            {
                if (documentIds.Count == 1)
                {
                    // 单个文档：精确匹配
                    filter = new
                    {
                        must = new[]
                        {
                            new
                            {
                                key = "document_id",
                                match = new { value = documentIds[0] }
                            }
                        }
                    };
                }
                else
                {
                    // 多个文档：OR 条件
                    var conditions = documentIds.Select(docId => new
                    {
                        key = "document_id",
                        match = new { value = docId }
                    }).ToArray();

                    filter = new { should = conditions };
                }
            }

            var requestObj = new
            {
                vector = vector,
                limit = topK,
                score_threshold = scoreThreshold,
                with_payload = true,
                with_vectors = false,
                filter = filter
            };

            var jsonContent = JsonSerializer.Serialize(requestObj, new JsonSerializerOptions 
            { 
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull 
            });
            
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonResponse = JsonDocument.Parse(responseContent);

            if (jsonResponse.RootElement.TryGetProperty("result", out var resultArray))
            {
                foreach (var item in resultArray.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idElement) &&
                        item.TryGetProperty("score", out var scoreElement))
                    {
                        var pointId = idElement.GetUInt64();
                        var score = scoreElement.GetSingle();

                        var payload = new Dictionary<string, object>();
                        if (item.TryGetProperty("payload", out var payloadObj))
                        {
                            foreach (var prop in payloadObj.EnumerateObject())
                            {
                                // 处理不同类型的值
                                object? value = prop.Value.ValueKind switch
                                {
                                    System.Text.Json.JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                                    System.Text.Json.JsonValueKind.Number => prop.Value.GetDouble(),
                                    System.Text.Json.JsonValueKind.True => true,
                                    System.Text.Json.JsonValueKind.False => false,
                                    System.Text.Json.JsonValueKind.Null => null,
                                    _ => prop.Value.GetRawText()
                                };
                                
                                if (value != null)
                                {
                                    payload[prop.Name] = value;
                                }
                            }
                        }

                        results.Add((pointId, score, payload));
                    }
                }
            }

            return results;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to search in Qdrant: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 删除点
    /// </summary>
    public async Task<bool> DeletePointAsync(string collectionName, ulong pointId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{_baseUrl}/collections/{collectionName}/points?wait=true";
            var request = new
            {
                points_selector = new
                {
                    points = new[] { pointId }
                }
            };

            var jsonContent = JsonSerializer.Serialize(request);
            var httpRequest = new HttpRequestMessage(HttpMethod.Delete, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to delete point from Qdrant: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取点数据
    /// </summary>
    public async Task<(float[] Vector, Dictionary<string, object> Payload)?> GetPointAsync(string collectionName, ulong pointId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{_baseUrl}/collections/{collectionName}/points/{pointId}";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonResponse = JsonDocument.Parse(responseContent);

            if (jsonResponse.RootElement.TryGetProperty("result", out var result))
            {
                float[]? vector = null;
                var payload = new Dictionary<string, object>();

                if (result.TryGetProperty("vector", out var vectorArray))
                {
                    vector = vectorArray.EnumerateArray()
                        .Select(v => v.GetSingle())
                        .ToArray();
                }

                if (result.TryGetProperty("payload", out var payloadObj))
                {
                    foreach (var prop in payloadObj.EnumerateObject())
                    {
                        payload[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }

                return vector != null ? (vector, payload) : null;
            }

            return null;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to get point from Qdrant: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 删除文档的所有向量点
    /// </summary>
    public async Task<bool> DeletePointsByDocumentIdAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{_baseUrl}/collections/{collectionName}/points/delete";
            
            var requestBody = new
            {
                filter = new
                {
                    must = new[]
                    {
                        new
                        {
                            key = "document_id",
                            match = new { value = documentId }
                        }
                    }
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to delete points by document ID: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 删除集合
    /// </summary>
    public async Task<bool> DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{_baseUrl}/collections/{collectionName}";
            var response = await _httpClient.DeleteAsync(url, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to delete Qdrant collection: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取集合信息
    /// </summary>
    public async Task<Dictionary<string, object>?> GetCollectionInfoAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{_baseUrl}/collections/{collectionName}";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonResponse = JsonDocument.Parse(responseContent);

            if (jsonResponse.RootElement.TryGetProperty("result", out var result))
            {
                var info = new Dictionary<string, object>();
                foreach (var prop in result.EnumerateObject())
                {
                    info[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
                return info;
            }

            return null;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to get Qdrant collection info: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 基于文本关键词搜索（BM25）
    /// </summary>
    public async Task<List<(ulong PointId, float Score, Dictionary<string, object> Payload)>> SearchByTextAsync(
        string collectionName,
        string queryText,
        int topK = 5,
        List<string>? documentIds = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(ulong, float, Dictionary<string, object>)>();

        try
        {
            topK = Math.Min(topK, QdrantConstants.MaxTopK);
            var url = $"{_baseUrl}/collections/{collectionName}/points/scroll";

            // 构建 filter（如果有 documentIds）
            object? filter = null;
            if (documentIds != null && documentIds.Count > 0)
            {
                if (documentIds.Count == 1)
                {
                    filter = new
                    {
                        must = new[]
                        {
                            new
                            {
                                key = "document_id",
                                match = new { value = documentIds[0] }
                            }
                        }
                    };
                }
                else
                {
                    var conditions = documentIds.Select(docId => new
                    {
                        key = "document_id",
                        match = new { value = docId }
                    }).ToArray();

                    filter = new { should = conditions };
                }
            }

            // 使用 scroll API + text filter 进行 BM25 搜索
            // 注意：Qdrant 的推荐方式是使用 search API 的 prefetch + query
            var searchUrl = $"{_baseUrl}/collections/{collectionName}/points/search";

            var requestObj = new
            {
                limit = topK,
                with_payload = true,
                with_vectors = false,
                filter = filter,
                // 使用文本查询进行 BM25 搜索
                query = new
                {
                    text = queryText
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestObj, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, searchUrl)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

            // 如果 search with text 不支持，回退到 scroll + filter
            if (!response.IsSuccessStatusCode)
            {
                return await SearchByTextScrollAsync(collectionName, queryText, topK, documentIds, cancellationToken);
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonResponse = JsonDocument.Parse(responseContent);

            if (jsonResponse.RootElement.TryGetProperty("result", out var resultArray))
            {
                foreach (var item in resultArray.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idElement) &&
                        item.TryGetProperty("score", out var scoreElement))
                    {
                        var pointId = idElement.GetUInt64();
                        var score = scoreElement.GetSingle();

                        var payload = new Dictionary<string, object>();
                        if (item.TryGetProperty("payload", out var payloadObj))
                        {
                            foreach (var prop in payloadObj.EnumerateObject())
                            {
                                object? value = prop.Value.ValueKind switch
                                {
                                    System.Text.Json.JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                                    System.Text.Json.JsonValueKind.Number => prop.Value.GetDouble(),
                                    System.Text.Json.JsonValueKind.True => true,
                                    System.Text.Json.JsonValueKind.False => false,
                                    System.Text.Json.JsonValueKind.Null => null,
                                    _ => prop.Value.GetRawText()
                                };

                                if (value != null)
                                {
                                    payload[prop.Name] = value;
                                }
                            }
                        }

                        results.Add((pointId, score, payload));
                    }
                }
            }

            return results;
        }
        catch (HttpRequestException ex)
        {
            // 出错时尝试回退方案
            return await SearchByTextScrollAsync(collectionName, queryText, topK, documentIds, cancellationToken);
        }
    }

    /// <summary>
    /// 回退方案：使用 Scroll API 进行简单的文本匹配搜索
    /// </summary>
    private async Task<List<(ulong PointId, float Score, Dictionary<string, object> Payload)>> SearchByTextScrollAsync(
        string collectionName,
        string queryText,
        int topK,
        List<string>? documentIds,
        CancellationToken cancellationToken)
    {
        var results = new List<(ulong, float, Dictionary<string, object>)>();

        try
        {
            var url = $"{_baseUrl}/collections/{collectionName}/points/scroll";

            // 构建请求
            var requestObj = new
            {
                limit = topK * 2, // 获取更多结果用于过滤
                with_payload = true,
                with_vectors = false,
                filter = documentIds != null && documentIds.Count > 0 ? new
                {
                    must = documentIds.Select(docId => new
                    {
                        key = "document_id",
                        match = new { value = docId }
                    }).ToArray()
                } : null
            };

            var jsonContent = JsonSerializer.Serialize(requestObj, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonResponse = JsonDocument.Parse(responseContent);

            if (jsonResponse.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("points", out var pointsArray))
            {
                var queryLower = queryText.ToLower();

                foreach (var item in pointsArray.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var idElement))
                        continue;

                    var pointId = idElement.GetUInt64();

                    var payload = new Dictionary<string, object>();
                    if (item.TryGetProperty("payload", out var payloadObj))
                    {
                        foreach (var prop in payloadObj.EnumerateObject())
                        {
                            object? value = prop.Value.ValueKind switch
                            {
                                System.Text.Json.JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                                System.Text.Json.JsonValueKind.Number => prop.Value.GetDouble(),
                                System.Text.Json.JsonValueKind.True => true,
                                System.Text.Json.JsonValueKind.False => false,
                                System.Text.Json.JsonValueKind.Null => null,
                                _ => prop.Value.GetRawText()
                            };

                            if (value != null)
                            {
                                payload[prop.Name] = value;
                            }
                        }
                    }

                    // 计算简单的文本匹配分数
                    float score = 0f;
                    if (payload.TryGetValue("content", out var contentObj) && contentObj is string content)
                    {
                        var contentLower = content.ToLower();
                        // 简单的 TF-IDF 风格评分
                        var queryTerms = queryLower.Split(new[] { ' ', ',', '.', '，', '。' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var term in queryTerms)
                        {
                            if (contentLower.Contains(term))
                            {
                                score += 1.0f;
                            }
                        }
                        score = score / Math.Max(queryTerms.Length, 1);
                    }

                    results.Add((pointId, score, payload));
                }
            }

            // 按分数排序并返回 topK
            return results
                .OrderByDescending(r => r.Item2) // 元组的第二项是 score
                .Take(topK)
                .ToList();
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException($"Failed to search by text in Qdrant");
        }
    }

    /// <summary>
    /// 更新集合的 Payload Schema（用于启用 BM25 全文搜索等）
    /// </summary>
    public async Task<bool> UpdatePayloadSchemaAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{_baseUrl}/collections/{collectionName}";

            // 先检查当前配置
            var existingSchema = await GetPayloadSchemaAsync(collectionName, cancellationToken);

            // 定义期望的 payload schema
            var desiredSchema = new Dictionary<string, object>
            {
                ["content"] = new
                {
                    type = "text"
                },
                ["document_id"] = new
                {
                    type = "keyword"
                }
            };

            // 检查是否需要更新
            bool needsUpdate = existingSchema == null ||
                              !existingSchema.ContainsKey("content") ||
                              !existingSchema.ContainsKey("document_id");

            if (!needsUpdate)
            {
                _logger.LogInformation("集合 {Collection} 的 Payload Schema 已是最新配置，无需更新", collectionName);
                return true;
            }

            _logger.LogInformation("开始更新集合 {Collection} 的 Payload Schema", collectionName);

            // Qdrant 使用 PATCH 方法更新集合配置
            var patchRequest = new
            {
                payload_schema = desiredSchema
            };

            var jsonContent = JsonSerializer.Serialize(patchRequest, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            using var httpRequest = new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("更新 Payload Schema 失败: {StatusCode}, {Error}",
                    response.StatusCode, errorContent);
                return false;
            }

            _logger.LogInformation("成功更新集合 {Collection} 的 Payload Schema", collectionName);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "更新 Payload Schema 时发生网络错误");
            return false;
        }
    }

    /// <summary>
    /// 获取集合的 Payload Schema 配置
    /// </summary>
    public async Task<Dictionary<string, object>?> GetPayloadSchemaAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{_baseUrl}/collections/{collectionName}";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("获取集合 {Collection} 信息失败: {StatusCode}",
                    collectionName, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonDoc = JsonDocument.Parse(content);

            // 解析 payload_schema
            if (jsonDoc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("config", out var config) &&
                config.TryGetProperty("params", out var paramsElem) &&
                paramsElem.TryGetProperty("payload_schema", out var schemaElem))
            {
                var schema = new Dictionary<string, object>();

                foreach (var prop in schemaElem.EnumerateObject())
                {
                    if (prop.Value.TryGetProperty("type", out var typeElem))
                    {
                        schema[prop.Name] = new Dictionary<string, string>
                        {
                            ["type"] = typeElem.GetString() ?? "unknown"
                        };
                    }
                }

                return schema;
            }

            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "获取 Payload Schema 时发生网络错误");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析 Payload Schema 响应失败");
            return null;
        }
    }
}

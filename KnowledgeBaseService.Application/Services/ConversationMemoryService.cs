using System.Text.Json;
using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Core.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace KnowledgeBaseService.Application.Services;

/// <summary>
/// 对话长期记忆服务实现
/// 核心流程：
/// 1. 保存记忆：提取摘要 → 向量化 → Qdrant 存储 → PostgreSQL 存储
/// 2. 检索记忆：向量检索（语义相似） + 结构化过滤（用户ID/类型） → 排序返回
/// 3. 记忆管理：重要性更新（时间衰减 + 访问强化）、过期清理
/// </summary>
public class ConversationMemoryService : IConversationMemoryService
{
    private readonly IEmbeddingClient _embeddingClient;
    private readonly ILLMChatClient _chatClient;
    private readonly IQdrantHttpClient _qdrantClient;
    private readonly ISqlSugarClient _dbClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConversationMemoryService> _logger;

    // Qdrant collection 名称（建议独立于知识库）
    private const string MemoryCollectionName = "conversation_memory_collection";

    // 时间衰减因子（天）
    private readonly double _decayFactor;

    // 最大记忆保留数量（每个用户）
    private readonly int _maxMemoriesPerUser;

    // 是否启用 LLM 摘要提取
    private readonly bool _enableLLMSummary;

    // 向量维度
    private readonly int _vectorDimension;

    // 当前最大 Point ID（用于生成唯一 ID）
    private static ulong _currentPointId = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static readonly object _pointIdLock = new object();

    public ConversationMemoryService(
        IEmbeddingClient embeddingClient,
        ILLMChatClient chatClient,
        IQdrantHttpClient qdrantClient,
        ISqlSugarClient dbClient,
        IConfiguration configuration,
        ILogger<ConversationMemoryService> logger)
    {
        _embeddingClient = embeddingClient ?? throw new ArgumentNullException(nameof(embeddingClient));
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _qdrantClient = qdrantClient ?? throw new ArgumentNullException(nameof(qdrantClient));
        _dbClient = dbClient ?? throw new ArgumentNullException(nameof(dbClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 读取配置
        _decayFactor = configuration.GetValue("Memory:DecayFactorDays", 30.0);
        _maxMemoriesPerUser = configuration.GetValue("Memory:MaxMemoriesPerUser", 1000);
        _enableLLMSummary = configuration.GetValue("Memory:EnableLLMSummary", true);
        _vectorDimension = configuration.GetValue("LLM:VectorDimension", 2560);
    }

    /// <summary>
    /// 初始化 Qdrant collection（在应用启动时调用）
    /// </summary>
    public async Task InitializeCollectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _qdrantClient.InitializeCollectionAsync(
                MemoryCollectionName,
                _vectorDimension,
                cancellationToken);

            _logger.LogInformation("成功初始化记忆 collection: {CollectionName}", MemoryCollectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化记忆 collection 失败");
            throw;
        }
    }

    /// <summary>
    /// 生成唯一的 Point ID
    /// </summary>
    private static ulong GeneratePointId()
    {
        lock (_pointIdLock)
        {
            return ++_currentPointId;
        }
    }

    public async Task<string> SaveMemoryAsync(
        string userId,
        string? sessionId,
        string userMessage,
        string assistantMessage,
        string memoryType = "fact",
        double importanceScore = 0.5,
        CancellationToken cancellationToken = default)
    {
        return await SaveMemoryWithMetadataAsync(
            userId, sessionId, userMessage, assistantMessage,
            memoryType, importanceScore, null, cancellationToken);
    }

    /// <summary>
    /// 保存对话记忆（带元数据，教学场景扩展）
    /// </summary>
    public async Task<string> SaveMemoryWithMetadataAsync(
        string userId,
        string? sessionId,
        string userMessage,
        string assistantMessage,
        string memoryType = "fact",
        double importanceScore = 0.5,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("用户ID不能为空", nameof(userId));

        if (string.IsNullOrWhiteSpace(userMessage))
            throw new ArgumentException("用户消息不能为空", nameof(userMessage));

        try
        {
            // 1. 根据记忆类型生成摘要（不同类型使用不同提示词）
            var summary = await ExtractMemorySummaryByTypeAsync(
                userMessage, assistantMessage, memoryType, metadata, cancellationToken);

            // 2. 准备向量化内容（摘要 + 用户问题）
            var vectorContent = summary + "\n\n原始问题：" + userMessage;

            // 3. 向量化
            var embeddingResult = await _embeddingClient.GetEmbeddingAsync(vectorContent, cancellationToken);

            // 4. 生成唯一 ID
            var memoryId = Guid.NewGuid().ToString();
            var pointId = GeneratePointId();

            // 5. 存入 Qdrant
            var payload = new Dictionary<string, object>
            {
                ["memory_id"] = memoryId,
                ["user_id"] = userId,
                ["session_id"] = sessionId ?? "",
                ["memory_type"] = memoryType,
                ["summary"] = summary,
                ["created_at"] = DateTime.UtcNow.ToString("O")
            };

            await _qdrantClient.UpsertPointAsync(
                MemoryCollectionName,
                pointId,
                embeddingResult.Vector!,
                payload,
                cancellationToken);

            // 6. 存入 PostgreSQL
            var memory = new ConversationMemory
            {
                Id = memoryId,
                UserId = userId,
                SessionId = sessionId,
                MemoryType = memoryType,
                Summary = summary,
                FullContent = JsonSerializer.Serialize(new
                {
                    user = userMessage,
                    assistant = assistantMessage
                }),
                VectorContent = vectorContent,
                VectorPointId = pointId.ToString(),
                ImportanceScore = importanceScore,
                Metadata = metadata != null ? JsonSerializer.Serialize(metadata) : null,
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow
            };

            await _dbClient.Insertable(memory).ExecuteCommandAsync(cancellationToken);

            _logger.LogInformation("成功保存记忆: UserId={UserId}, MemoryId={MemoryId}, Type={Type}",
                userId, memoryId, memoryType);

            return memoryId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存记忆失败: UserId={UserId}", userId);
            throw new InvalidOperationException("保存记忆失败", ex);
        }
    }

    public async Task<List<MemorySearchResult>> RetrieveMemoriesAsync(
        string userId,
        string query,
        int topK = 5,
        double minScore = 0.6,
        string? memoryType = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("用户ID不能为空", nameof(userId));

        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("查询不能为空", nameof(query));

        try
        {
            // 1. 向量化查询
            var embeddingResult = await _embeddingClient.GetEmbeddingAsync(query, cancellationToken);

            // 2. 向量检索（使用现有的 SearchAsync，无法直接按 user_id 过滤，需后处理）
            var searchResults = await _qdrantClient.SearchAsync(
                MemoryCollectionName,
                embeddingResult.Vector!,
                topK * 5, // 多检索一些，后面过滤
                (float)minScore,
                null, // 无文档 ID 过滤
                cancellationToken);

            if (searchResults == null || searchResults.Count() == 0)
            {
                _logger.LogInformation("未找到相关记忆: UserId={UserId}, Query={Query}", userId, query);
                return new List<MemorySearchResult>();
            }

            // 3. 按 user_id 过滤（后处理）
            var filteredResults = searchResults
                .Where(r => r.Payload.ContainsKey("user_id") &&
                           r.Payload["user_id"]?.ToString() == userId)
                .ToList();

            // 如果指定了记忆类型，进一步过滤
            if (!string.IsNullOrWhiteSpace(memoryType))
            {
                filteredResults = filteredResults
                    .Where(r => r.Payload.ContainsKey("memory_type") &&
                               r.Payload["memory_type"]?.ToString() == memoryType)
                    .ToList();
            }

            if (filteredResults.Count() == 0)
            {
                _logger.LogInformation("过滤后未找到相关记忆: UserId={UserId}, Query={Query}", userId, query);
                return new List<MemorySearchResult>();
            }

            // 4. 从数据库获取完整记忆信息
            var memoryIds = filteredResults
                .Select(r => r.Payload.ContainsKey("memory_id") ? r.Payload["memory_id"]?.ToString() : null)
                .Where(id => !string.IsNullOrEmpty(id))
                .Cast<string>()
                .ToList();

            if (memoryIds.Count() == 0)
                return new List<MemorySearchResult>();

            var memories = await _dbClient.Queryable<ConversationMemory>()
                .Where(m => memoryIds.Contains(m.Id) && !m.IsDeleted)
                .ToListAsync(cancellationToken);

            // 5. 合并向量检索结果和数据库信息
            var results = new List<MemorySearchResult>();
            foreach (var searchResult in filteredResults)
            {
                var memoryId = searchResult.Payload.ContainsKey("memory_id")
                    ? searchResult.Payload["memory_id"]?.ToString()
                    : null;

                if (string.IsNullOrEmpty(memoryId))
                    continue;

                var memory = memories.FirstOrDefault(m => m.Id == memoryId);
                if (memory == null)
                    continue;

                results.Add(new MemorySearchResult
                {
                    Id = memory.Id,
                    UserId = memory.UserId,
                    SessionId = memory.SessionId,
                    MemoryType = memory.MemoryType,
                    Summary = memory.Summary,
                    FullContent = memory.FullContent,
                    SimilarityScore = searchResult.Score,
                    ImportanceScore = memory.ImportanceScore,
                    CreatedAt = memory.CreatedAt,
                    LastAccessedAt = memory.LastAccessedAt
                });
            }

            // 6. 更新访问记录（异步，不阻塞）
            _ = Task.Run(async () =>
            {
                foreach (var result in results)
                {
                    await UpdateMemoryImportanceAsync(result.Id, CancellationToken.None);
                }
            }, cancellationToken);

            // 7. 按综合得分排序（相似度 * 重要性）
            return results
                .OrderByDescending(r => r.SimilarityScore * r.ImportanceScore)
                .Take(topK)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检索记忆失败: UserId={UserId}, Query={Query}", userId, query);
            throw new InvalidOperationException("检索记忆失败", ex);
        }
    }

    public async Task<List<MemorySearchResult>> GetRecentMemoriesAsync(
        string userId,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("用户ID不能为空", nameof(userId));

        try
        {
            var memories = await _dbClient.Queryable<ConversationMemory>()
                .Where(m => m.UserId == userId && !m.IsDeleted)
                .OrderBy(m => m.CreatedAt, OrderByType.Desc)
                .Take(count)
                .ToListAsync(cancellationToken);

            return memories.Select(m => new MemorySearchResult
            {
                Id = m.Id,
                UserId = m.UserId,
                SessionId = m.SessionId,
                MemoryType = m.MemoryType,
                Summary = m.Summary,
                FullContent = m.FullContent,
                SimilarityScore = 1.0, // 时间序列检索，设为最高
                ImportanceScore = m.ImportanceScore,
                CreatedAt = m.CreatedAt,
                LastAccessedAt = m.LastAccessedAt
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取最近记忆失败: UserId={UserId}", userId);
            throw new InvalidOperationException("获取最近记忆失败", ex);
        }
    }

    public async Task UpdateMemoryImportanceAsync(
        string memoryId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var memory = await _dbClient.Queryable<ConversationMemory>()
                .Where(m => m.Id == memoryId)
                .FirstAsync(cancellationToken);

            if (memory == null)
                return;

            // 更新访问次数
            memory.AccessCount++;
            memory.LastAccessedAt = DateTime.UtcNow;

            // 计算时间衰减：importance = base_score * (1 - days_since_last_access / decay_factor)
            var daysSinceCreation = (DateTime.UtcNow - memory.CreatedAt).TotalDays;
            var decayMultiplier = Math.Max(0, 1 - daysSinceCreation / _decayFactor);

            // 访问强化：每次访问增加 0.05，最高 0.3
            var accessBoost = Math.Min(0.05 * memory.AccessCount, 0.3);

            memory.ImportanceScore = Math.Min(1.0,
                memory.ImportanceScore * decayMultiplier + accessBoost);

            await _dbClient.Updateable(memory)
                .UpdateColumns(m => new { m.AccessCount, m.LastAccessedAt, m.ImportanceScore })
                .ExecuteCommandAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "更新记忆重要性失败: MemoryId={MemoryId}", memoryId);
            // 不抛出异常，允许失败
        }
    }

    public async Task CleanupMemoriesAsync(
        string userId,
        int keepTopN = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("用户ID不能为空", nameof(userId));

        try
        {
            // 1. 删除过期记忆
            var expiredCount = await _dbClient.Deleteable<ConversationMemory>()
                .Where(m => m.UserId == userId && m.ExpiresAt != null && m.ExpiresAt < DateTime.UtcNow)
                .ExecuteCommandAsync(cancellationToken);

            if (expiredCount > 0)
                _logger.LogInformation("清理过期记忆: UserId={UserId}, Count={Count}", userId, expiredCount);

            // 2. 获取所有记忆，按重要性和访问时间排序
            var allMemories = await _dbClient.Queryable<ConversationMemory>()
                .Where(m => m.UserId == userId && !m.IsDeleted)
                .OrderBy(m => m.ImportanceScore, OrderByType.Desc)
                .OrderBy(m => m.LastAccessedAt, OrderByType.Desc)
                .ToListAsync(cancellationToken);

            if (allMemories.Count > keepTopN)
            {
                var toDelete = allMemories.Skip(keepTopN).ToList();
                var deleteIds = toDelete.Select(m => m.Id).ToList();

                // 软删除
                await _dbClient.Updateable<ConversationMemory>()
                    .SetColumns(m => m.IsDeleted == true)
                    .Where(m => deleteIds.Contains(m.Id))
                    .ExecuteCommandAsync(cancellationToken);

                // 从 Qdrant 删除
                foreach (var mem in toDelete)
                {
                    if (!string.IsNullOrEmpty(mem.VectorPointId) && ulong.TryParse(mem.VectorPointId, out var pointId))
                    {
                        try
                        {
                            await _qdrantClient.DeletePointAsync(MemoryCollectionName, pointId, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "删除 Qdrant 点失败: PointId={PointId}", pointId);
                        }
                    }
                }

                _logger.LogInformation("清理低重要性记忆: UserId={UserId}, Count={Count}",
                    userId, toDelete.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理记忆失败: UserId={UserId}", userId);
            throw new InvalidOperationException("清理记忆失败", ex);
        }
    }

    public async Task DeleteUserMemoriesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("用户ID不能为空", nameof(userId));

        try
        {
            // 1. 获取所有记忆的 Point ID
            var memories = await _dbClient.Queryable<ConversationMemory>()
                .Where(m => m.UserId == userId && !m.IsDeleted)
                .ToListAsync(cancellationToken);

            if (memories.Count == 0)
                return;

            // 2. 从 Qdrant 删除
            foreach (var mem in memories)
            {
                if (!string.IsNullOrEmpty(mem.VectorPointId) && ulong.TryParse(mem.VectorPointId, out var pointId))
                {
                    try
                    {
                        await _qdrantClient.DeletePointAsync(MemoryCollectionName, pointId, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "删除 Qdrant 点失败: PointId={PointId}", pointId);
                    }
                }
            }

            // 3. 从数据库删除（物理删除）
            await _dbClient.Deleteable<ConversationMemory>()
                .Where(m => m.UserId == userId)
                .ExecuteCommandAsync(cancellationToken);

            _logger.LogInformation("删除用户所有记忆: UserId={UserId}, Count={Count}",
                userId, memories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除用户记忆失败: UserId={UserId}", userId);
            throw new InvalidOperationException("删除用户记忆失败", ex);
        }
    }

    #region 记忆摘要提取（分类型）

    /// <summary>
    /// 根据记忆类型提取摘要（教学场景扩展）
    /// </summary>
    private async Task<string> ExtractMemorySummaryByTypeAsync(
        string userMessage,
        string assistantMessage,
        string memoryType,
        Dictionary<string, object>? metadata,
        CancellationToken cancellationToken)
    {
        // 如果禁用 LLM 摘要或消息过短，使用简单截取
        if (!_enableLLMSummary || userMessage.Length < 20)
        {
            return TruncateText(userMessage, 200);
        }

        try
        {
            // 根据记忆类型选择不同的提示词
            var (systemPrompt, maxTokens) = GetPromptByMemoryType(memoryType, metadata);

            var userPrompt = BuildUserPrompt(userMessage, assistantMessage, memoryType, metadata);

            var messages = new List<ChatMessage>
            {
                new ChatMessage(MessageRole.System, systemPrompt),
                new ChatMessage(MessageRole.User, userPrompt)
            };

            var summary = await _chatClient.GetCompletionAsync(
                messages,
                temperature: 0.3f,
                maxTokens: maxTokens,
                cancellationToken: cancellationToken);

            // 清理和验证摘要
            summary = CleanSummary(summary);

            if (string.IsNullOrWhiteSpace(summary) ||
                summary.Contains("无需记忆") ||
                summary.Length < 5)
            {
                return TruncateText(userMessage, 200);
            }

            _logger.LogDebug("LLM 摘要提取成功: Type={Type}, Summary={Summary}", memoryType, summary);
            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM 摘要提取失败，回退到简单截取");
            return TruncateText(userMessage, 200);
        }
    }

    /// <summary>
    /// 根据记忆类型获取对应的系统提示词
    /// </summary>
    private (string systemPrompt, int maxTokens) GetPromptByMemoryType(
        string memoryType,
        Dictionary<string, object>? metadata)
    {
        return memoryType switch
        {
            // ========== 教学场景专用类型 ==========
            "exam_analysis" => (
                "你是一个教学数据分析助手。从老师与系统的对话中提取试卷分析的关键结论。" +
                "提取规则：" +
                "1. 提取关键指标（平均分、及格率、最高分、最低分、提交人数等）；" +
                "2. 识别异常情况（如某题错误率过高、某学生异常表现）；" +
                "3. 提取老师关注的分析维度；" +
                "4. 摘要格式：【试卷名称】关键数据...分析结论...；" +
                "5. 长度控制在80-150字；" +
                "6. 如果只是简单问答无分析价值，返回\"无需记忆\"。",
                200),
            
            "student_profile" => (
                "你是一个学生画像助手。从老师对话中提取关于特定学生的关键信息。" +
                "提取规则：" +
                "1. 提取学生姓名/学号；" +
                "2. 记录学业表现特征（成绩趋势、擅长/薄弱科目）；" +
                "3. 记录行为特征（出勤、作业提交情况）；" +
                "4. 标注需重点关注的原因；" +
                "5. 摘要格式：【学生姓名】表现特征...关注点...；" +
                "6. 长度控制在50-100字。",
                150),
            
            "class_summary" => (
                "你是一个班级数据汇总助手。从老师对话中提取班级整体情况的关键信息。" +
                "提取规则：" +
                "1. 提取班级标识（年级、班级名称）；" +
                "2. 记录整体表现数据（人数、完成率、平均水平）；" +
                "3. 与其他班级的对比结论（如有）；" +
                "4. 摘要格式：【班级名称】整体情况...；" +
                "5. 长度控制在50-100字。",
                150),
            
            "answer_pattern" => (
                "你是一个答题规律分析助手。从老师对话中提取学生答题的规律和问题。" +
                "提取规则：" +
                "1. 识别高错误率的题目及其知识点；" +
                "2. 分析错误类型（计算错误、概念混淆、审题不清等）；" +
                "3. 识别共性问题（多数学生犯的错误）；" +
                "4. 摘要格式：【题目/知识点】错误规律...原因分析...；" +
                "5. 长度控制在80-150字。",
                200),
            
            "teaching_insight" => (
                "你是一个教学洞察助手。从老师对话中提取对教学有指导意义的结论。" +
                "提取规则：" +
                "1. 提取教学效果评估结论；" +
                "2. 记录老师得出的教学调整建议；" +
                "3. 识别知识点的难易程度反馈；" +
                "4. 摘要格式：【知识点/章节】教学洞察...建议...；" +
                "5. 长度控制在50-120字。",
                180),
            
            // ========== 通用类型 ==========
            "preference" => (
                "你是一个偏好提取助手。从对话中提取用户的偏好信息。" +
                "提取规则：" +
                "1. 识别用户喜欢查看的数据维度（如及格率、排名、进步幅度）；" +
                "2. 识别用户偏好的展示方式（详细/简洁、图表/文字）；" +
                "3. 识别用户关注的重点对象（如后进生、优等生）；" +
                "4. 摘要格式：用户偏好...；" +
                "5. 长度控制在30-80字。",
                120),
            
            "context" => (
                "你是一个对话上下文摘要助手。总结对话的核心内容。" +
                "提取规则：" +
                "1. 概括用户的主要问题和系统的回答；" +
                "2. 保留关键数据和结论；" +
                "3. 去除寒暄和无关内容；" +
                "4. 长度控制在50-100字。",
                150),
            
            // 默认：fact 类型
            _ => (
                "你是一个记忆提取助手。从用户对话中提取关键信息，生成简洁的记忆摘要。" +
                "提取规则：" +
                "1. 识别用户提到的事实信息（职业、身份、所教科目等）；" +
                "2. 识别提到的关键实体（人名、班级、学校等）；" +
                "3. 摘要长度控制在50-100字；" +
                "4. 使用陈述句；" +
                "5. 只提取有价值的、可复用的信息；" +
                "6. 如果对话中没有值得记忆的信息，返回\"无需记忆\"。",
                150)
        };
    }

    /// <summary>
    /// 构建用户提示词（包含元数据上下文）
    /// </summary>
    private string BuildUserPrompt(
        string userMessage,
        string assistantMessage,
        string memoryType,
        Dictionary<string, object>? metadata)
    {
        var prompt = "请从以下对话中提取关键记忆：\n\n";

        // 如果有元数据，添加上下文
        if (metadata != null && metadata.Count > 0)
        {
            prompt += "【上下文信息】\n";
            
            if (metadata.TryGetValue("examId", out var examId))
                prompt += "试卷ID：" + examId + "\n";
            
            if (metadata.TryGetValue("examName", out var examName))
                prompt += "试卷名称：" + examName + "\n";
            
            if (metadata.TryGetValue("classId", out var classId))
                prompt += "班级ID：" + classId + "\n";
            
            if (metadata.TryGetValue("className", out var className))
                prompt += "班级名称：" + className + "\n";
            
            if (metadata.TryGetValue("subject", out var subject))
                prompt += "学科：" + subject + "\n";
            
            if (metadata.TryGetValue("studentName", out var studentName))
                prompt += "学生姓名：" + studentName + "\n";

            if (metadata.TryGetValue("metrics", out var metrics))
                prompt += "关键指标：" + JsonSerializer.Serialize(metrics) + "\n";
            
            prompt += "\n";
        }

        prompt += "【老师问题】\n" + userMessage + "\n\n";
        prompt += "【系统回复】\n" + assistantMessage + "\n\n";
        prompt += "请提取摘要：";

        return prompt;
    }

    #endregion

    /// <summary>
    /// 提取记忆摘要（通用方法，保持向后兼容）
    /// </summary>
    private async Task<string> ExtractMemorySummaryAsync(
        string userMessage,
        string assistantMessage,
        CancellationToken cancellationToken)
    {
        return await ExtractMemorySummaryByTypeAsync(
            userMessage, assistantMessage, "fact", null, cancellationToken);
    }

    /// <summary>
    /// 清理 LLM 返回的摘要文本
    /// </summary>
    private static string CleanSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return string.Empty;

        // 去除首尾空白
        summary = summary.Trim();

        // 去除可能的引号包裹
        if ((summary.StartsWith("\"") && summary.EndsWith("\"")) ||
            (summary.StartsWith("'") && summary.EndsWith("'")))
        {
            summary = summary.Substring(1, summary.Length - 2);
        }

        // 去除常见的前缀标记
        string[] prefixesToRemove = { "摘要：", "摘要:", "记忆：", "记忆:", "提取结果：", "提取结果:" };
        foreach (var prefix in prefixesToRemove)
        {
            if (summary.StartsWith(prefix))
            {
                summary = summary.Substring(prefix.Length).TrimStart();
                break;
            }
        }

        return summary;
    }

    /// <summary>
    /// 截取文本到指定长度
    /// </summary>
    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = text.Trim();

        if (text.Length <= maxLength)
            return text;

        // 尝试在句子边界截断
        var truncated = text.Substring(0, maxLength);
        var lastPunctuation = truncated.LastIndexOfAny(new[] { '。', '！', '？', '.', '!', '?', '，', ',' });

        if (lastPunctuation > maxLength / 2)
        {
            return truncated.Substring(0, lastPunctuation + 1);
        }

        return truncated + "...";
    }
}

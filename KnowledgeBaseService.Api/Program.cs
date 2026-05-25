using KnowledgeBaseService.Api;
using KnowledgeBaseService.Application.Services;
using KnowledgeBaseService.Infrastructure.Clients;
using KnowledgeBaseService.Infrastructure.Repositories;
using KnowledgeBaseService.Infrastructure.SemanticKernel;
using KnowledgeBaseService.Infrastructure.Cache;
using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Application.Options;
using StackExchange.Redis;
using SqlSugar;
using Serilog;
using KnowledgeBaseService.Application.DTOs;

var builder = WebApplication.CreateBuilder(args);

// 配置 Serilog
var logPath = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logPath);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "KnowledgeBaseService.API")
        .WriteTo.Console()
        .WriteTo.File(
            path: Path.Combine(logPath, "app-.log"),
            rollingInterval: Serilog.RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}",
            retainedFileCountLimit: 30,
            fileSizeLimitBytes: 104857600  // 100MB
        );
});

// 日志配置（兼容 Microsoft.Logging）
builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

// 添加 API 文档支持
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // 配置 JSON 序列化：不区分大小写，使用 camelCase
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ========== RAG 服务配置 ==========
// 通过配置选择 RAG 实现方式：
// - UseSemanticKernel: true  -> 使用 Semantic Kernel 实现
// - UseSemanticKernel: false -> 使用原始实现
var useSemanticKernel = builder.Configuration.GetValue<bool>("RAG:UseSemanticKernel", false);

if (useSemanticKernel)
{
    // 使用 Semantic Kernel 版本的 RAG 服务
    // 内部已注册 IEmbeddingClient（语义分块器需要）
    builder.Services.AddSemanticKernelRAG(builder.Configuration);
}
else
{
    // 使用原始 RAG 实现
    builder.Services
        .AddHttpClient<IEmbeddingClient, DoubaoEmbeddingClient>()
        .ConfigureHttpClient((sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

    builder.Services
        .AddHttpClient<ILLMChatClient, DoubaoChatClient>()
        .ConfigureHttpClient((sp, client) =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });

    builder.Services
        .AddHttpClient<IQdrantHttpClient, QdrantHttpClient>()
        .ConfigureHttpClient((sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

    // 注册文本分块器
    builder.Services.AddScoped<ITextSplitter, TextSplitter>();

    // 注册语义分块器（可选，根据配置启用）
    var useSemanticChunking = builder.Configuration.GetValue<bool>("RAG:UseSemanticChunking", false);
    if (useSemanticChunking)
    {
        // 注册 Redis 缓存服务
        var redisEnabled = builder.Configuration.GetValue<bool>("Redis:Enabled", true);
        if (redisEnabled)
        {
            builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var connectionString = config.GetConnectionString("Redis") ?? config["Redis:ConnectionString"];
                return ConnectionMultiplexer.Connect(connectionString);
            });
            builder.Services.AddScoped<KnowledgeBaseService.Application.Interfaces.ICacheService, KnowledgeBaseService.Infrastructure.Cache.RedisCacheService>();
        }

        // 注册语义分块器配置选项
        builder.Services.Configure<SemanticChunkingOptions>(
            builder.Configuration.GetSection("RAG:SemanticChunking"));

        // 注册优化后的语义分块器
        builder.Services.AddScoped<ISemanticTextSplitter, SemanticTextSplitterOptimized>();
    }

    // 注册混合检索服务（可选，根据配置启用）
    var useHybridSearch = builder.Configuration.GetValue<bool>("RAG:UseHybridSearch", false);
    if (useHybridSearch)
    {
        builder.Services.AddScoped<IHybridSearchService, HybridSearchService>();
    }

    builder.Services.AddScoped<IRAGService, RAGService>();
}

// 视觉模型客户端（两种模式都需要）
builder.Services
    .AddHttpClient<IDoubaoVisionClient, DoubaoVisionClient>()
    .ConfigureHttpClient((sp, client) =>
    {
        client.Timeout = TimeSpan.FromMinutes(2);  // 视觉模型可能需要更长时间
    });

// 注册应用服务
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IFileImportService, FileImportService>();
builder.Services.AddScoped<IDocumentVersionService, DocumentVersionService>();

// 注册后台任务队列（单例）
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();

// 注册文档导入后台服务
builder.Services.AddHostedService<KnowledgeBaseService.Api.BackgroundServices.DocumentImportBackgroundService>();

// 注册 SqlSugar
builder.Services.AddScoped<ISqlSugarClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("DefaultConnection");
    
    var db = new SqlSugarClient(new ConnectionConfig()
    {
        ConnectionString = connectionString,
        DbType = DbType.PostgreSQL,
        IsAutoCloseConnection = true,
        InitKeyType = InitKeyType.Attribute
    });

    // Code First: 自动建表（如果表不存在则创建）
    db.CodeFirst.InitTables(
        typeof(KnowledgeBaseService.Core.Entities.Document),
        typeof(KnowledgeBaseService.Core.Entities.DocumentVersion)
    );
    
    return db;
});

// 注册仓储
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IDocumentVersionRepository, DocumentVersionRepository>();

// 注册依赖初始化
builder.Services.AddHostedService<ServiceInitializationHostedService>();

// 配置路由（将 URL 转换为小写）
builder.Services.AddRouting(options => options.LowercaseUrls = true);

// CORS 配置（允许所有来源、方法、请求头）
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()           // 允许任何来源
            .AllowAnyMethod()           // 允许任何 HTTP 方法
            .AllowAnyHeader();          // 允许任何请求头
    });
});

var app = builder.Build();

// 中间件配置（顺序很重要！CORS 要在最前面）
app.UseCors("AllowAll");

//if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseWebSockets();
app.MapControllers();

// 健康检查端点
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("Health");

app.Run();

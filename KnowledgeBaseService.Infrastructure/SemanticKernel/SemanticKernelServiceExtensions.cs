using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Application.Services;
using KnowledgeBaseService.Infrastructure.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Qdrant;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;

namespace KnowledgeBaseService.Infrastructure.SemanticKernel;

/// <summary>
/// Semantic Kernel 服务注册扩展
/// 支持任何 OpenAI 兼容的 LLM 提供商
/// </summary>
public static class SemanticKernelServiceExtensions
{
    /// <summary>
    /// 添加 Semantic Kernel RAG 服务
    /// 替换原有的 RAG 实现，支持通过配置切换不同的 LLM 提供商
    /// 支持语义分块和混合检索
    /// </summary>
    public static IServiceCollection AddSemanticKernelRAG(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册 HttpClient（用于 OpenAI 兼容服务）
        services.AddHttpClient<OpenAICompatibleEmbeddingService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddSingleton<ITextEmbeddingGenerationService>(sp =>
            sp.GetRequiredService<OpenAICompatibleEmbeddingService>());

        services.AddHttpClient<OpenAICompatibleChatCompletionService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddSingleton<IChatCompletionService>(sp =>
            sp.GetRequiredService<OpenAICompatibleChatCompletionService>());

        // 注册 Semantic Kernel
        services.AddSingleton<Kernel>(sp =>
        {
            var embeddingService = sp.GetRequiredService<ITextEmbeddingGenerationService>();
            var chatService = sp.GetRequiredService<IChatCompletionService>();

            var kernelBuilder = Kernel.CreateBuilder();

            // 添加自定义服务
            kernelBuilder.Services.AddSingleton(embeddingService);
            kernelBuilder.Services.AddSingleton(chatService);

            return kernelBuilder.Build();
        });

        // 注册 Qdrant Memory Store
        services.AddSingleton<IMemoryStore>(sp =>
        {
            var qdrantEndpoint = configuration["Qdrant:Endpoint"] ?? "http://localhost:6333";
            // 从配置读取向量维度（不同 LLM 提供商的 Embedding 维度不同）
            var vectorDimension = configuration.GetValue<int>("LLM:VectorDimension", 1536);

            // 创建 Qdrant Memory Store
            return new QdrantMemoryStore(
                endpoint: qdrantEndpoint,
                vectorSize: vectorDimension);
        });

        // 注册 Semantic Text Memory
        services.AddSingleton<ISemanticTextMemory>(sp =>
        {
            var memoryStore = sp.GetRequiredService<IMemoryStore>();
            var embeddingService = sp.GetRequiredService<ITextEmbeddingGenerationService>();

            return new SemanticTextMemory(memoryStore, embeddingService);
        });

        // 注册 Qdrant HttpClient（用于混合检索的 BM25 搜索）
        services.AddHttpClient<IQdrantHttpClient, QdrantHttpClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // 注册 IEmbeddingClient（语义分块器需要）
        // 注意：SemanticKernel 使用自己的 TextEmbeddingGenerationService，但我们的 SemanticTextSplitter 需要 IEmbeddingClient
        services.AddHttpClient<IEmbeddingClient, DoubaoEmbeddingClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        // 注册语义分块器（可选）
        var useSemanticChunking = configuration.GetValue<bool>("RAG:UseSemanticChunking", false);
        if (useSemanticChunking)
        {
            services.AddScoped<ISemanticTextSplitter, SemanticTextSplitter>();
        }

        // 注册混合检索服务（可选）
        var useHybridSearch = configuration.GetValue<bool>("RAG:UseHybridSearch", false);
        if (useHybridSearch)
        {
            services.AddScoped<IHybridSearchService, HybridSearchService>();
        }

        // 注册 Semantic Kernel RAG Service（替换原有的 RAGService）
        services.AddScoped<IRAGService, SemanticKernelRAGService>();

        return services;
    }

    /// <summary>
    /// 添加原始 RAG 服务（非 Semantic Kernel 版本）
    /// </summary>
    public static IServiceCollection AddOriginalRAG(this IServiceCollection services)
    {
        services.AddScoped<IRAGService, RAGService>();
        return services;
    }
}

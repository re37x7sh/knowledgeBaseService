using KnowledgeBaseService.Infrastructure.Clients;
using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Core.Constants;

namespace KnowledgeBaseService.Api;

/// <summary>
/// 服务初始化后台服务
/// 启动时初始化 Qdrant 集合
/// </summary>
public class ServiceInitializationHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServiceInitializationHostedService> _logger;

    public ServiceInitializationHostedService(IServiceProvider serviceProvider, ILogger<ServiceInitializationHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var qdrantClient = scope.ServiceProvider.GetRequiredService<IQdrantHttpClient>();
                
                _logger.LogInformation("Initializing Qdrant collection...");
                await qdrantClient.InitializeCollectionAsync(
                    "documents",
                    VectorDimensions.DeepSeekEmbedding,
                    cancellationToken);
                
                _logger.LogInformation("Qdrant collection initialized successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize services");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

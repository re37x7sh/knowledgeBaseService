using KnowledgeBaseService.Application.DTOs;
using KnowledgeBaseService.Application.Interfaces;
using KnowledgeBaseService.Application.Services;
using KnowledgeBaseService.Core.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace KnowledgeBaseService.UnitTests.Services;

public class DocumentServiceTests
{
    private readonly Mock<IDocumentRepository> _mockRepository;
    private readonly Mock<IDocumentVersionService> _mockVersionService;
    private readonly Mock<ILogger<DocumentService>> _mockLogger;
    private readonly DocumentService _service;

    public DocumentServiceTests()
    {
        _mockRepository = new Mock<IDocumentRepository>();
        _mockVersionService = new Mock<IDocumentVersionService>();
        _mockLogger = new Mock<ILogger<DocumentService>>();
        _service = new DocumentService(_mockRepository.Object, _mockVersionService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsResponse()
    {
        // Arrange
        var request = new CreateDocumentRequest
        {
            Title = "Test Document",
            Content = "This is a test content",
            Category = "Test",
            SourceUrl = "http://example.com"
        };

        _mockRepository.Setup(r => r.InsertAsync(It.IsAny<Document>()))
            .ReturnsAsync(true);

        _mockVersionService.Setup(v => v.CreateVersionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentVersion());

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(request.Title, result.Title);
        Assert.Equal(request.Category, result.Category);
        _mockRepository.Verify(r => r.InsertAsync(It.IsAny<Document>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_InvalidRequest_ThrowsException()
    {
        // Arrange
        var request = new CreateDocumentRequest { Title = "", Content = "" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(request));
    }

    [Fact]
    public async Task GetAsync_ExistingId_ReturnsDocument()
    {
        // Arrange
        var document = new Document
        {
            Id = "test-id",
            Title = "Test",
            Content = "Content"
        };

        _mockRepository.Setup(r => r.GetByIdAsync("test-id"))
            .ReturnsAsync(document);

        // Act
        var result = await _service.GetAsync("test-id");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-id", result.Id);
    }

    [Fact]
    public async Task GetAsync_NonExistingId_ReturnsNull()
    {
        // Arrange
        _mockRepository.Setup(r => r.GetByIdAsync("non-existing"))
            .ReturnsAsync((Document?)null);

        // Act
        var result = await _service.GetAsync("non-existing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_ValidId_ReturnsTrue()
    {
        // Arrange
        _mockRepository.Setup(r => r.DeleteAsync("test-id"))
            .ReturnsAsync(true);

        // Act
        var result = await _service.DeleteAsync("test-id");

        // Assert
        Assert.True(result);
        _mockRepository.Verify(r => r.DeleteAsync("test-id"), Times.Once);
    }
}

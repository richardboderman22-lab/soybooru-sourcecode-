using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.Data;
using Nuuru.Server.Models;
using Nuuru.Server.Models.Booru;
using Nuuru.Server.Services;
using Nuuru.Server.Services.Search;
using Nuuru.Server.Services.Storage;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class PostServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ILogger<PostService>> _mockLogger;
    private readonly Mock<IFileStorageService> _mockFileStorageService;
    private readonly Mock<ITagService> _mockTagService;
    private readonly Mock<IThumbnailService> _mockThumbnailService;
    private readonly Mock<IWatchService> _mockWatchService;
    private readonly PostService _sut;

    public PostServiceTests()
    {
        _context = TestDbContextFactory.Create();
        _mockLogger = new Mock<ILogger<PostService>>();
        _mockFileStorageService = new Mock<IFileStorageService>();
        _mockTagService = new Mock<ITagService>();
        _mockThumbnailService = new Mock<IThumbnailService>();
        _mockWatchService = new Mock<IWatchService>();
        var settingsService = new UserSettingsService(_context);
        var mockDefaultQueryFilter = new Mock<IDefaultQueryFilterService>();
        mockDefaultQueryFilter
            .Setup(x => x.ApplyDefaultFiltersAsync(It.IsAny<IQueryable<Post>>()))
            .ReturnsAsync((IQueryable<Post> q) => q);
        _sut = new PostService(_context, _mockLogger.Object, _mockFileStorageService.Object, _mockTagService.Object, _mockThumbnailService.Object, _mockWatchService.Object, settingsService, mockDefaultQueryFilter.Object, new Mock<IBointsService>().Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task CreatePostAsync_WithValidUser_ReturnsPost()
    {
        // Arrange
        var user = MockData.CreateTestUser("testuser", "test@example.com");
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();

        var fileStream = MockData.CreateTestImageStream();
        var fileName = "test.jpg";

        _mockFileStorageService
            .Setup(x => x.SaveFileAsync(It.IsAny<Stream>(), fileName, It.IsAny<FileStorageOptions>()))
            .ReturnsAsync(new FileStorageResult
            {
                Success = true,
                FileIdentifier = "storage/123.jpg",
                Metadata = new FileMetadata
                {
                    FileIdentifier = "storage/123.jpg",
                    Hash = "uniquehash123",
                    ContentType = "image/jpeg",
                    FileSize = 102400,
                    OriginalFileName = fileName,
                    CreatedAtUtc = DateTime.UtcNow
                }
            });

        // Act
        var result = await _sut.CreatePostAsync(fileStream, fileName, "image/jpeg", user.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.Post.Should().NotBeNull();
        result.Post!.ImageHash.Should().Be("uniquehash123");
        result.Post.MimeType.Should().Be("image/jpeg");
        result.Post.FileSize.Should().Be(102400);
        result.Post.StorageIdentifier.Should().Be("storage/123.jpg");

        // Verify file was saved
        _mockFileStorageService.Verify(
            x => x.SaveFileAsync(fileStream, fileName, It.IsAny<FileStorageOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task CreatePostAsync_WithNonExistentUser_ReturnsNull()
    {
        // Arrange
        var fileStream = MockData.CreateTestImageStream();
        var fileName = "test.jpg";
        var nonExistentUserId = Guid.NewGuid();

        // Act
        var result = await _sut.CreatePostAsync(fileStream, fileName, "image/jpeg", nonExistentUserId);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Uploader account not found");

        // Verify file storage was not called
        _mockFileStorageService.Verify(
            x => x.SaveFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<FileStorageOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task CreatePostAsync_WhenFileStorageFails_ReturnsNull()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();

        var fileStream = MockData.CreateTestImageStream();
        var fileName = "test.jpg";

        _mockFileStorageService
            .Setup(x => x.SaveFileAsync(It.IsAny<Stream>(), fileName, It.IsAny<FileStorageOptions>()))
            .ReturnsAsync(new FileStorageResult
            {
                Success = false,
                ErrorMessage = "Storage error"
            });

        // Act
        var result = await _sut.CreatePostAsync(fileStream, fileName, "image/jpeg", user.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Failed to save file");
    }

    [Fact]
    public async Task CreatePostAsync_WithDuplicateHash_RejectsUpload()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        await _context.Users.AddAsync(user);

        // Create existing post with same hash
        var existingPost = MockData.CreateTestPost(user, 1, "duplicatehash");
        await _context.BooruPosts.AddAsync(existingPost);
        await _context.SaveChangesAsync();

        var fileStream = MockData.CreateTestImageStream();
        var fileName = "duplicate.jpg";

        _mockFileStorageService
            .Setup(x => x.SaveFileAsync(It.IsAny<Stream>(), fileName, It.IsAny<FileStorageOptions>()))
            .ReturnsAsync(new FileStorageResult
            {
                Success = true,
                FileIdentifier = "storage/456.jpg",
                Metadata = new FileMetadata
                {
                    FileIdentifier = "storage/456.jpg",
                    Hash = "duplicatehash", // Same hash as existing post
                    ContentType = "image/jpeg",
                    FileSize = 102400,
                    OriginalFileName = fileName,
                    CreatedAtUtc = DateTime.UtcNow
                }
            });

        // Act
        var result = await _sut.CreatePostAsync(fileStream, fileName, "image/jpeg", user.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("A post with this file already exists");
    }

    [Fact]
    public async Task GetPostByIdAsync_WithExistingPost_ReturnsPost()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var post = MockData.CreateTestPost(user, 1);
        await _context.Users.AddAsync(user);
        await _context.BooruPosts.AddAsync(post);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetPostByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.ImageHash.Should().Be("testhash123");
    }

    [Fact]
    public async Task GetPostByIdAsync_WithNonExistentPost_ReturnsNull()
    {
        // Act
        var result = await _sut.GetPostByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeletePostAsync_WithExistingPost_DeletesPostAndFile()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var post = MockData.CreateTestPost(user, 1);
        await _context.Users.AddAsync(user);
        await _context.BooruPosts.AddAsync(post);
        await _context.SaveChangesAsync();

        _mockFileStorageService
            .Setup(x => x.DeleteFileAsync(post.StorageIdentifier))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.DeletePostAsync(1);

        // Assert
        result.Should().BeTrue();

        // Verify post is deleted from database
        var deletedPost = await _context.BooruPosts.FindAsync(1);
        deletedPost.Should().BeNull();

        // Verify file deletion was attempted
        _mockFileStorageService.Verify(x => x.DeleteFileAsync(post.StorageIdentifier), Times.Once);
    }

    [Fact]
    public async Task DeletePostAsync_WithNonExistentPost_ReturnsFalse()
    {
        // Act
        var result = await _sut.DeletePostAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeletePostAsync_WhenFileDeleteFails_StillDeletesPostFromDatabase()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var post = MockData.CreateTestPost(user, 1);
        await _context.Users.AddAsync(user);
        await _context.BooruPosts.AddAsync(post);
        await _context.SaveChangesAsync();

        _mockFileStorageService
            .Setup(x => x.DeleteFileAsync(post.StorageIdentifier))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.DeletePostAsync(1);

        // Assert
        result.Should().BeTrue();

        // Verify post is still deleted from database even if file deletion failed
        var deletedPost = await _context.BooruPosts.FindAsync(1);
        deletedPost.Should().BeNull();
    }

    [Fact]
    public async Task PostExistsAsync_WithExistingHash_ReturnsTrue()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var post = MockData.CreateTestPost(user, 1, "existinghash");
        await _context.Users.AddAsync(user);
        await _context.BooruPosts.AddAsync(post);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.PostExistsAsync("existinghash");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task PostExistsAsync_WithNonExistentHash_ReturnsFalse()
    {
        // Act
        var result = await _sut.PostExistsAsync("nonexistenthash");

        // Assert
        result.Should().BeFalse();
    }
}

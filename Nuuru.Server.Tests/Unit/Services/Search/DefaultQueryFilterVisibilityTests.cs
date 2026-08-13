using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.Auth;
using Nuuru.Server.Data;
using Nuuru.Server.Models;
using Nuuru.Server.Models.Booru;
using Nuuru.Server.Services;
using Nuuru.Server.Services.Search;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services.Search;

public class DefaultQueryFilterVisibilityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly Mock<IUserSettingsService> _userSettingsServiceMock;
    private readonly IMemoryCache _cache;
    private readonly Mock<ILogger<DefaultQueryFilterService>> _loggerMock;
    private readonly ApplicationUser _user;

    public DefaultQueryFilterVisibilityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _userSettingsServiceMock = new Mock<IUserSettingsService>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _loggerMock = new Mock<ILogger<DefaultQueryFilterService>>();

        _user = MockData.CreateTestUser();
        _context.Users.Add(_user);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private DefaultQueryFilterService CreateService(ICurrentUserContext userContext)
    {
        return new DefaultQueryFilterService(
            _context,
            userContext,
            _userSettingsServiceMock.Object,
            _cache,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ApplyDefaultFiltersAsync_ExcludesTrashedPosts_ForAnonymousUser()
    {
        // Arrange
        var visiblePost = MockData.CreateTestPost(_user, 1, "hash1");
        var trashedPost = MockData.CreateTestPost(_user, 2, "hash2");
        trashedPost.IsTrashed = true;
        _context.BooruPosts.AddRange(visiblePost, trashedPost);
        await _context.SaveChangesAsync();

        var service = CreateService(new AnonymousUserContext());

        // Act
        var query = await service.ApplyDefaultFiltersAsync(_context.BooruPosts.AsQueryable());
        var results = await query.ToListAsync();

        // Assert
        results.Should().ContainSingle(p => p.Id == 1);
        results.Should().NotContain(p => p.Id == 2);
    }

    [Fact]
    public async Task ApplyDefaultFiltersAsync_ExcludesPendingPosts_ForAnonymousUser()
    {
        // Arrange
        var approvedPost = MockData.CreateTestPost(_user, 1, "hash1", isApproved: true);
        var pendingPost = MockData.CreateTestPost(_user, 2, "hash2", isApproved: false);
        _context.BooruPosts.AddRange(approvedPost, pendingPost);
        await _context.SaveChangesAsync();

        var service = CreateService(new AnonymousUserContext());

        // Act
        var query = await service.ApplyDefaultFiltersAsync(_context.BooruPosts.AsQueryable());
        var results = await query.ToListAsync();

        // Assert
        results.Should().ContainSingle(p => p.Id == 1);
        results.Should().NotContain(p => p.Id == 2);
    }

    [Fact]
    public async Task ApplyDefaultFiltersAsync_AllowsOwnPendingPosts()
    {
        // Arrange
        var approvedPost = MockData.CreateTestPost(_user, 1, "hash1", isApproved: true);
        var pendingPost = MockData.CreateTestPost(_user, 2, "hash2", isApproved: false);
        _context.BooruPosts.AddRange(approvedPost, pendingPost);
        await _context.SaveChangesAsync();

        var userContext = new Mock<ICurrentUserContext>();
        userContext.Setup(c => c.UserId).Returns(_user.Id);
        userContext.Setup(c => c.HasPermission(It.IsAny<string>())).Returns(false);

        var service = CreateService(userContext.Object);

        // Act
        var query = await service.ApplyDefaultFiltersAsync(_context.BooruPosts.AsQueryable());
        var results = await query.ToListAsync();

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain(p => p.Id == 2); // Own pending post should be visible
    }

    [Fact]
    public async Task IsPostVisibleAsync_ReturnsFalse_WhenDefaultQueryExcludesRating()
    {
        var safePost = MockData.CreateTestPost(_user, 1, "hash1");
        var explicitPost = MockData.CreateTestPost(_user, 2, "hash2");
        explicitPost.Rating = PostRating.Explicit;

        _context.BooruPosts.AddRange(safePost, explicitPost);
        await _context.SaveChangesAsync();

        var userContext = new Mock<ICurrentUserContext>();
        userContext.Setup(c => c.UserId).Returns(_user.Id);
        userContext.Setup(c => c.HasPermission(It.IsAny<string>())).Returns(false);
        _userSettingsServiceMock
            .Setup(s => s.GetDefaultSearchQueryAsync(_user.Id))
            .ReturnsAsync("-rating:explicit");

        var service = CreateService(userContext.Object);

        (await service.IsPostVisibleAsync(1)).Should().BeTrue();
        (await service.IsPostVisibleAsync(2)).Should().BeFalse();
    }

    [Fact]
    public async Task GetVisiblePostIdsAsync_ReturnsOnlyIdsAllowedByDefaultQuery()
    {
        var safePost = MockData.CreateTestPost(_user, 1, "hash1");
        var explicitPost = MockData.CreateTestPost(_user, 2, "hash2");
        explicitPost.Rating = PostRating.Explicit;
        var questionablePost = MockData.CreateTestPost(_user, 3, "hash3");
        questionablePost.Rating = PostRating.Questionable;

        _context.BooruPosts.AddRange(safePost, explicitPost, questionablePost);
        await _context.SaveChangesAsync();

        var userContext = new Mock<ICurrentUserContext>();
        userContext.Setup(c => c.UserId).Returns(_user.Id);
        userContext.Setup(c => c.HasPermission(It.IsAny<string>())).Returns(false);
        _userSettingsServiceMock
            .Setup(s => s.GetDefaultSearchQueryAsync(_user.Id))
            .ReturnsAsync("-rating:explicit");

        var service = CreateService(userContext.Object);
        var visibleIds = await service.GetVisiblePostIdsAsync([1, 2, 3]);

        visibleIds.Should().BeEquivalentTo([1, 3]);
    }
}

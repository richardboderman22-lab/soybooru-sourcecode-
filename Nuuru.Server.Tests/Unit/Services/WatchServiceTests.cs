using FluentAssertions;
using Nuuru.Server.Models;
using Nuuru.Server.Services;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class WatchServiceTests
{
    private readonly WatchService _sut;
    private readonly Nuuru.Server.Data.ApplicationDbContext _context;

    private static readonly Guid User1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid User2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public WatchServiceTests()
    {
        _context = TestDbContextFactory.CreateWithSeedData();
        _sut = new WatchService(_context);
    }

    [Fact]
    public async Task ToggleWatch_WhenNotWatching_ReturnsTrue()
    {
        // Act
        var result = await _sut.ToggleWatchAsync(User1Id, WatchTargetType.BooruPost, 1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ToggleWatch_WhenAlreadyWatching_ReturnsFalse()
    {
        // Arrange
        await _sut.ToggleWatchAsync(User1Id, WatchTargetType.BooruPost, 1);

        // Act
        var result = await _sut.ToggleWatchAsync(User1Id, WatchTargetType.BooruPost, 1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsWatching_WhenWatching_ReturnsTrue()
    {
        // Arrange
        await _sut.ToggleWatchAsync(User1Id, WatchTargetType.BooruPost, 1);

        // Act
        var result = await _sut.IsWatchingAsync(User1Id, WatchTargetType.BooruPost, 1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsWatching_WhenNotWatching_ReturnsFalse()
    {
        // Act
        var result = await _sut.IsWatchingAsync(User1Id, WatchTargetType.BooruPost, 1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetWatcherUserIds_ReturnsCorrectUserIds()
    {
        // Arrange
        await _sut.ToggleWatchAsync(User1Id, WatchTargetType.BooruPost, 1);
        await _sut.ToggleWatchAsync(User2Id, WatchTargetType.BooruPost, 1);

        // Act
        var result = (await _sut.GetWatcherUserIdsAsync(WatchTargetType.BooruPost, 1)).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(User1Id);
        result.Should().Contain(User2Id);
    }

    [Fact]
    public async Task GetWatchCount_ReturnsCorrectCount()
    {
        // Arrange
        await _sut.ToggleWatchAsync(User1Id, WatchTargetType.BooruPost, 1);
        await _sut.ToggleWatchAsync(User2Id, WatchTargetType.BooruPost, 1);

        // Act
        var result = await _sut.GetWatchCountAsync(WatchTargetType.BooruPost, 1);

        // Assert
        result.Should().Be(2);
    }

    [Fact]
    public async Task GetWatchCount_DifferentTargetTypes_AreIndependent()
    {
        // Arrange
        await _sut.ToggleWatchAsync(User1Id, WatchTargetType.BooruPost, 1);
        await _sut.ToggleWatchAsync(User1Id, WatchTargetType.ForumThread, 1);

        // Act
        var postCount = await _sut.GetWatchCountAsync(WatchTargetType.BooruPost, 1);
        var threadCount = await _sut.GetWatchCountAsync(WatchTargetType.ForumThread, 1);

        // Assert
        postCount.Should().Be(1);
        threadCount.Should().Be(1);
    }
}

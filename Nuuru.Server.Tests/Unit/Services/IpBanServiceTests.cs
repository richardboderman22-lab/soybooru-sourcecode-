using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.Data;
using Nuuru.Server.Models;
using Nuuru.Server.Services;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class IpBanServiceTests
{
    private readonly ApplicationDbContext _context;
    private readonly IpBanService _sut;
    private readonly IMemoryCache _cache;

    private static readonly Guid User1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public IpBanServiceTests()
    {
        _context = TestDbContextFactory.CreateWithSeedData();
        _cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new Mock<ILogger<IpBanService>>();
        _sut = new IpBanService(_context, logger.Object, _cache);
    }

    #region CreateIpBanAsync

    [Fact]
    public async Task CreateIpBan_ReturnsNewBan()
    {
        // Act
        var ban = await _sut.CreateIpBanAsync("192.168.1.1", "Test reason");

        // Assert
        ban.Should().NotBeNull();
        ban.IpAddress.Should().Be("192.168.1.1");
        ban.Reason.Should().Be("Test reason");
        ban.Active.Should().BeTrue();
        ban.EndTime.Should().Be(DateTime.MaxValue);
    }

    [Fact]
    public async Task CreateIpBan_WithExpiry_SetsEndTime()
    {
        // Arrange
        var until = DateTime.UtcNow.AddDays(7);

        // Act
        var ban = await _sut.CreateIpBanAsync("10.0.0.1", "Temporary ban", until);

        // Assert
        ban.EndTime.Should().Be(until);
    }

    [Fact]
    public async Task CreateIpBan_WithCreatedBy_SetsCreatedById()
    {
        // Act
        var ban = await _sut.CreateIpBanAsync("10.0.0.2", "Mod ban", createdById: User1Id);

        // Assert
        ban.CreatedById.Should().Be(User1Id);
    }

    [Fact]
    public async Task CreateIpBan_InvalidatesCache()
    {
        // Arrange - prime the cache with a "not banned" result
        var ip = "192.168.1.100";
        await _sut.IsIpBannedAsync(ip); // caches false

        // Act
        await _sut.CreateIpBanAsync(ip, "Now banned");
        var isBanned = await _sut.IsIpBannedAsync(ip);

        // Assert - should re-query and find the ban
        isBanned.Should().BeTrue();
    }

    [Fact]
    public async Task CreateIpBan_PersistsToDatabase()
    {
        // Act
        var ban = await _sut.CreateIpBanAsync("172.16.0.1", "Persisted");

        // Assert
        var found = await _context.IpBans.FindAsync(ban.Id);
        found.Should().NotBeNull();
        found!.IpAddress.Should().Be("172.16.0.1");
    }

    #endregion

    #region IsIpBannedAsync

    [Fact]
    public async Task IsIpBanned_WhenBanned_ReturnsTrue()
    {
        // Arrange
        await _sut.CreateIpBanAsync("192.168.1.1", "Banned");

        // Act
        var result = await _sut.IsIpBannedAsync("192.168.1.1");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsIpBanned_WhenNotBanned_ReturnsFalse()
    {
        // Act
        var result = await _sut.IsIpBannedAsync("192.168.1.1");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsIpBanned_ExpiredBan_ReturnsFalse()
    {
        // Arrange - create a ban that's already expired
        var ban = new IpBan
        {
            IpAddress = "10.0.0.50",
            Reason = "Expired",
            StartTime = DateTime.UtcNow.AddDays(-2),
            EndTime = DateTime.UtcNow.AddDays(-1),
            Active = true
        };
        _context.IpBans.Add(ban);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.IsIpBannedAsync("10.0.0.50");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsIpBanned_InactiveBan_ReturnsFalse()
    {
        // Arrange - create a ban that's deactivated
        var ban = new IpBan
        {
            IpAddress = "10.0.0.51",
            Reason = "Deactivated",
            StartTime = DateTime.UtcNow.AddDays(-1),
            EndTime = DateTime.UtcNow.AddDays(7),
            Active = false
        };
        _context.IpBans.Add(ban);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.IsIpBannedAsync("10.0.0.51");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsIpBanned_FutureBan_ReturnsFalse()
    {
        // Arrange - ban that hasn't started yet
        var ban = new IpBan
        {
            IpAddress = "10.0.0.52",
            Reason = "Future",
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(7),
            Active = true
        };
        _context.IpBans.Add(ban);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.IsIpBannedAsync("10.0.0.52");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsIpBanned_DifferentIp_ReturnsFalse()
    {
        // Arrange
        await _sut.CreateIpBanAsync("192.168.1.1", "Banned");

        // Act
        var result = await _sut.IsIpBannedAsync("192.168.1.2");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsIpBanned_UsesCacheOnSecondCall()
    {
        // Arrange
        await _sut.CreateIpBanAsync("192.168.1.1", "Banned");

        // Act - first call primes cache, second should use it
        var result1 = await _sut.IsIpBannedAsync("192.168.1.1");
        var result2 = await _sut.IsIpBannedAsync("192.168.1.1");

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
    }

    #endregion

    #region GetActiveIpBanAsync

    [Fact]
    public async Task GetActiveIpBan_WhenBanned_ReturnsBan()
    {
        // Arrange
        await _sut.CreateIpBanAsync("192.168.1.1", "Active ban");

        // Act
        var ban = await _sut.GetActiveIpBanAsync("192.168.1.1");

        // Assert
        ban.Should().NotBeNull();
        ban!.IpAddress.Should().Be("192.168.1.1");
        ban.Reason.Should().Be("Active ban");
    }

    [Fact]
    public async Task GetActiveIpBan_WhenNotBanned_ReturnsNull()
    {
        // Act
        var ban = await _sut.GetActiveIpBanAsync("192.168.1.1");

        // Assert
        ban.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveIpBan_MultipleBans_ReturnsMostRecent()
    {
        // Arrange
        var ban1 = new IpBan
        {
            IpAddress = "10.0.0.60",
            Reason = "First ban",
            StartTime = DateTime.UtcNow.AddDays(-2),
            EndTime = DateTime.UtcNow.AddDays(5),
            Active = true
        };
        var ban2 = new IpBan
        {
            IpAddress = "10.0.0.60",
            Reason = "Second ban",
            StartTime = DateTime.UtcNow.AddDays(-1),
            EndTime = DateTime.UtcNow.AddDays(7),
            Active = true
        };
        _context.IpBans.AddRange(ban1, ban2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetActiveIpBanAsync("10.0.0.60");

        // Assert
        result.Should().NotBeNull();
        result!.Reason.Should().Be("Second ban");
    }

    #endregion

    #region RemoveIpBanAsync

    [Fact]
    public async Task RemoveIpBan_ExistingBan_ReturnsTrue()
    {
        // Arrange
        var ban = await _sut.CreateIpBanAsync("192.168.1.1", "To remove");

        // Act
        var result = await _sut.RemoveIpBanAsync(ban.Id);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveIpBan_ExistingBan_DeactivatesBan()
    {
        // Arrange
        var ban = await _sut.CreateIpBanAsync("192.168.1.1", "To remove");

        // Act
        await _sut.RemoveIpBanAsync(ban.Id);

        // Assert
        var updated = await _context.IpBans.FindAsync(ban.Id);
        updated!.Active.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveIpBan_ExistingBan_InvalidatesCache()
    {
        // Arrange
        var ban = await _sut.CreateIpBanAsync("192.168.1.1", "To remove");
        await _sut.IsIpBannedAsync("192.168.1.1"); // prime cache with true

        // Act
        await _sut.RemoveIpBanAsync(ban.Id);
        var isBanned = await _sut.IsIpBannedAsync("192.168.1.1");

        // Assert
        isBanned.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveIpBan_NonExistentBan_ReturnsFalse()
    {
        // Act
        var result = await _sut.RemoveIpBanAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetActiveIpBansAsync

    [Fact]
    public async Task GetActiveIpBans_ReturnsActiveBans()
    {
        // Arrange
        await _sut.CreateIpBanAsync("192.168.1.1", "Ban 1");
        await _sut.CreateIpBanAsync("192.168.1.2", "Ban 2");

        // Act
        var (bans, totalCount) = await _sut.GetActiveIpBansAsync(1, 20);

        // Assert
        totalCount.Should().Be(2);
        bans.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetActiveIpBans_ExcludesInactiveBans()
    {
        // Arrange
        await _sut.CreateIpBanAsync("192.168.1.1", "Active");
        var removedBan = await _sut.CreateIpBanAsync("192.168.1.2", "Removed");
        await _sut.RemoveIpBanAsync(removedBan.Id);

        // Act
        var (bans, totalCount) = await _sut.GetActiveIpBansAsync(1, 20);

        // Assert
        totalCount.Should().Be(1);
        bans.Should().ContainSingle(b => b.IpAddress == "192.168.1.1");
    }

    [Fact]
    public async Task GetActiveIpBans_ExcludesExpiredBans()
    {
        // Arrange
        await _sut.CreateIpBanAsync("192.168.1.1", "Active");
        var ban = new IpBan
        {
            IpAddress = "10.0.0.70",
            Reason = "Expired",
            StartTime = DateTime.UtcNow.AddDays(-2),
            EndTime = DateTime.UtcNow.AddDays(-1),
            Active = true
        };
        _context.IpBans.Add(ban);
        await _context.SaveChangesAsync();

        // Act
        var (bans, totalCount) = await _sut.GetActiveIpBansAsync(1, 20);

        // Assert
        totalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetActiveIpBans_SearchByIp_FiltersResults()
    {
        // Arrange
        await _sut.CreateIpBanAsync("192.168.1.1", "Ban 1");
        await _sut.CreateIpBanAsync("10.0.0.1", "Ban 2");

        // Act
        var (bans, totalCount) = await _sut.GetActiveIpBansAsync(1, 20, search: "192.168");

        // Assert
        totalCount.Should().Be(1);
        bans.Should().ContainSingle(b => b.IpAddress == "192.168.1.1");
    }

    [Fact]
    public async Task GetActiveIpBans_MatchIpAddresses_FiltersResults()
    {
        // Arrange
        await _sut.CreateIpBanAsync("192.168.1.1", "Ban 1");
        await _sut.CreateIpBanAsync("10.0.0.1", "Ban 2");

        // Act
        var (bans, totalCount) = await _sut.GetActiveIpBansAsync(
            1, 20, matchIpAddresses: new HashSet<string> { "10.0.0.1" });

        // Assert
        totalCount.Should().Be(1);
        bans.Should().ContainSingle(b => b.IpAddress == "10.0.0.1");
    }

    [Fact]
    public async Task GetActiveIpBans_Pagination_ReturnsCorrectPage()
    {
        // Arrange
        for (int i = 1; i <= 5; i++)
            await _sut.CreateIpBanAsync($"192.168.1.{i}", $"Ban {i}");

        // Act
        var (bans, totalCount) = await _sut.GetActiveIpBansAsync(2, 2);

        // Assert
        totalCount.Should().Be(5);
        bans.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetActiveIpBans_OrderedByStartTimeDescending()
    {
        // Arrange
        var ban1 = new IpBan
        {
            IpAddress = "10.0.0.80",
            Reason = "Old",
            StartTime = DateTime.UtcNow.AddHours(-2),
            EndTime = DateTime.UtcNow.AddDays(7),
            Active = true
        };
        var ban2 = new IpBan
        {
            IpAddress = "10.0.0.81",
            Reason = "New",
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow.AddDays(7),
            Active = true
        };
        _context.IpBans.AddRange(ban1, ban2);
        await _context.SaveChangesAsync();

        // Act
        var (bans, _) = await _sut.GetActiveIpBansAsync(1, 20);
        var banList = bans.ToList();

        // Assert
        banList[0].Reason.Should().Be("New");
        banList[1].Reason.Should().Be("Old");
    }

    #endregion

    #region GetDistinctBannedIpAddressesAsync

    [Fact]
    public async Task GetDistinctBannedIpAddresses_ReturnsUniqueActiveIps()
    {
        // Arrange
        await _sut.CreateIpBanAsync("192.168.1.1", "Ban 1");
        await _sut.CreateIpBanAsync("192.168.1.2", "Ban 2");
        await _sut.CreateIpBanAsync("192.168.1.1", "Duplicate IP ban");

        // Act
        var ips = (await _sut.GetDistinctBannedIpAddressesAsync()).ToList();

        // Assert
        ips.Should().HaveCount(2);
        ips.Should().Contain("192.168.1.1");
        ips.Should().Contain("192.168.1.2");
    }

    [Fact]
    public async Task GetDistinctBannedIpAddresses_ExcludesInactive()
    {
        // Arrange
        await _sut.CreateIpBanAsync("192.168.1.1", "Active");
        var removed = await _sut.CreateIpBanAsync("192.168.1.2", "Removed");
        await _sut.RemoveIpBanAsync(removed.Id);

        // Act
        var ips = (await _sut.GetDistinctBannedIpAddressesAsync()).ToList();

        // Assert
        ips.Should().ContainSingle().Which.Should().Be("192.168.1.1");
    }

    #endregion

    #region InvalidateCache

    [Fact]
    public async Task InvalidateCache_RemovesCachedValue()
    {
        // Arrange
        await _sut.CreateIpBanAsync("192.168.1.1", "Banned");
        await _sut.IsIpBannedAsync("192.168.1.1"); // prime cache

        // Act
        _sut.InvalidateCache("192.168.1.1");

        // Assert - cache key should be gone (we can verify indirectly by checking it re-queries)
        var cacheKey = $"ipban:192.168.1.1";
        _cache.TryGetValue(cacheKey, out _).Should().BeFalse();
    }

    #endregion
}

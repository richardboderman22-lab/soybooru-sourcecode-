using FluentAssertions;
using Nuuru.Server.Data;
using Nuuru.Server.Models;
using Nuuru.Server.Services;
using Nuuru.Server.Tests.Helpers;
using Xunit;

namespace Nuuru.Server.Tests.Unit.Services;

public class AuditLogServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly AuditLogService _sut;

    public AuditLogServiceTests()
    {
        _context = TestDbContextFactory.Create();
        _sut = new AuditLogService(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task<AuditLog> SeedAuditLogAsync(string action, string username, string ipAddress = "127.0.0.1", string targetId = "1")
    {
        var user = MockData.CreateTestUser(username);
        await _context.Users.AddAsync(user);

        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            Action = action,
            Category = "Test",
            UserId = user.Id,
            User = user,
            IpAddress = ipAddress,
            TargetType = "Post",
            TargetId = targetId,
            HttpMethod = "POST",
            RequestPath = "/api/test",
            ResponseStatusCode = 200,
            Timestamp = DateTime.UtcNow
        };

        await _context.AuditLogs.AddAsync(log);
        await _context.SaveChangesAsync();
        return log;
    }

    [Fact]
    public async Task GetLogsAsync_PartialSearch_ReturnsMatches()
    {
        await SeedAuditLogAsync("CreatePost", "Alice", "192.168.1.1", "100");
        await SeedAuditLogAsync("DeletePost", "Bob", "192.168.1.2", "101");

        var (items, totalCount) = await _sut.GetLogsAsync(username: "Al", exact: false);

        items.Should().HaveCount(1);
        items.First().User!.UserName.Should().Be("Alice");
        totalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetLogsAsync_ExactSearch_ReturnsOnlyExactMatch()
    {
        await SeedAuditLogAsync("CreatePost", "Alice", "192.168.1.1", "100");
        await SeedAuditLogAsync("DeletePost", "Alicia", "192.168.1.2", "101");

        var (items, totalCount) = await _sut.GetLogsAsync(username: "Alice", exact: true);

        items.Should().HaveCount(1);
        items.First().User!.UserName.Should().Be("Alice");
        totalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetLogsAsync_ExactSearch_NoMatchWhenPartial()
    {
        await SeedAuditLogAsync("CreatePost", "Alice", "192.168.1.1", "100");

        var (items, totalCount) = await _sut.GetLogsAsync(username: "Al", exact: true);

        items.Should().BeEmpty();
        totalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetLogsAsync_ExactSearch_ActionMatch()
    {
        await SeedAuditLogAsync("CreatePost", "Alice");
        await SeedAuditLogAsync("CreateComment", "Bob");

        var (items, totalCount) = await _sut.GetLogsAsync(action: "CreatePost", exact: true);

        items.Should().HaveCount(1);
        items.First().Action.Should().Be("CreatePost");
        totalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetLogsAsync_ExactSearch_IpAddressMatch()
    {
        await SeedAuditLogAsync("Action", "User", "127.0.0.1");
        await SeedAuditLogAsync("Action", "User", "127.0.0.11");

        var (items, totalCount) = await _sut.GetLogsAsync(ipAddress: "127.0.0.1", exact: true);

        items.Should().HaveCount(1);
        items.First().IpAddress.Should().Be("127.0.0.1");
        totalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetLogsAsync_ExactSearch_TargetIdMatch()
    {
        await SeedAuditLogAsync("Action", "User", "127.0.0.1", "10");
        await SeedAuditLogAsync("Action", "User", "127.0.0.1", "100");

        var (items, totalCount) = await _sut.GetLogsAsync(targetId: "10", exact: true);

        items.Should().HaveCount(1);
        items.First().TargetId.Should().Be("10");
        totalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetLogsAsync_PartialSearch_CompositeTargetIdMatch()
    {
        await SeedAuditLogAsync("Action", "User1", "127.0.0.1", "general:1:123");
        await SeedAuditLogAsync("Action", "User2", "127.0.0.1", "1234");
        await SeedAuditLogAsync("Action", "User3", "127.0.0.1", "other:99:5");

        // Should match in last part of hierarchical ID
        var (items, totalCount) = await _sut.GetLogsAsync(targetId: "12", exact: false);
        items.Should().HaveCount(2); // general:1:123 (123 contains 12), 1234 (1234 contains 12)
        totalCount.Should().Be(2);

        // Should NOT match in middle part of hierarchical ID
        (items, totalCount) = await _sut.GetLogsAsync(targetId: "99", exact: false);
        items.Should().BeEmpty(); // "99" is middle part of other:99:5, but not in last part (5)
        totalCount.Should().Be(0);

        // Should match if no colon present
        (items, totalCount) = await _sut.GetLogsAsync(targetId: "123", exact: false);
        items.Should().HaveCount(2); // general:1:123, 1234 (1234 contains 123)
        totalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetLogsAsync_ExactSearch_CompositeTargetIdMatch()
    {
        await SeedAuditLogAsync("Action", "User1", "127.0.0.1", "general:1:123");
        await SeedAuditLogAsync("Action", "User2", "127.0.0.1", "general:1:1234");
        await SeedAuditLogAsync("Action", "User3", "127.0.0.1", "other:1:123");
        await SeedAuditLogAsync("Action", "User4", "127.0.0.1", "123");

        // Match exact part (postId) - it's the last part or the only part
        var (items, totalCount) = await _sut.GetLogsAsync(targetId: "123", exact: true);
        items.Should().HaveCount(3); // general:1:123, other:1:123, 123
        totalCount.Should().Be(3);

        // Should NOT match middle part (threadId) with exact search
        (items, totalCount) = await _sut.GetLogsAsync(targetId: "1", exact: true);
        items.Should().BeEmpty();
        totalCount.Should().Be(0);

        // Should NOT match first part (categorySlug) with exact search
        (items, totalCount) = await _sut.GetLogsAsync(targetId: "general", exact: true);
        items.Should().BeEmpty();
        totalCount.Should().Be(0);

        // Should NOT match partial part
        (items, totalCount) = await _sut.GetLogsAsync(targetId: "12", exact: true);
        items.Should().BeEmpty();
        totalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetLogsAsync_ExactSearch_ArrowCompositeTargetIdMatch()
    {
        await SeedAuditLogAsync("Action", "User5", "127.0.0.1", "OldTag -> NewTag");
        await SeedAuditLogAsync("Action", "User6", "127.0.0.1", "AnotherOldTag -> AnotherNewTag");

        // Match exact part (source)
        var (items, totalCount) = await _sut.GetLogsAsync(targetId: "OldTag", exact: true);
        items.Should().HaveCount(1);
        items.First().TargetId.Should().Be("OldTag -> NewTag");

        // Match exact part (target)
        (items, totalCount) = await _sut.GetLogsAsync(targetId: "NewTag", exact: true);
        items.Should().HaveCount(1);
        items.First().TargetId.Should().Be("OldTag -> NewTag");

        // Should NOT match partial part
        (items, totalCount) = await _sut.GetLogsAsync(targetId: "Old", exact: true);
        items.Should().BeEmpty();
    }
}

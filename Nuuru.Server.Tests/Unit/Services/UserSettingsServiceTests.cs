using FluentAssertions;
using Nuuru.Server.Data;
using Nuuru.Server.Models;
using Nuuru.Server.Services;
using Nuuru.Server.Services.Search;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class UserSettingsServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly UserSettingsService _sut;

    public UserSettingsServiceTests()
    {
        _context = TestDbContextFactory.Create();
        _sut = new UserSettingsService(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task GetDefaultSearchQueryAsync_WhenBabyModeDisabled_ReturnsStoredQuery()
    {
        var user = MockData.CreateTestUser();
        _context.Users.Add(user);
        _context.UserSettings.Add(new UserSettings
        {
            UserId = user.Id,
            DefaultSearchQuery = "foo"
        });
        await _context.SaveChangesAsync();

        var query = await _sut.GetDefaultSearchQueryAsync(user.Id);

        query.Should().Be("foo");
    }

    [Fact]
    public async Task GetDefaultSearchQueryAsync_WhenBabyModeEnabled_AppendsDefaultQuery()
    {
        var user = MockData.CreateTestUser();
        user.IsBabyMode = true;
        _context.Users.Add(user);
        _context.UserSettings.Add(new UserSettings
        {
            UserId = user.Id,
            DefaultSearchQuery = "foo"
        });
        await _context.SaveChangesAsync();

        var query = await _sut.GetDefaultSearchQueryAsync(user.Id);

        query.Should().Be($"foo {SearchDefaults.DefaultQuery}");
    }

    [Fact]
    public async Task GetDefaultSearchQueryAsync_WhenBabyModeEnabledAndNoStoredQuery_ReturnsDefaultQuery()
    {
        var user = MockData.CreateTestUser();
        user.IsBabyMode = true;
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var query = await _sut.GetDefaultSearchQueryAsync(user.Id);

        query.Should().Be(SearchDefaults.DefaultQuery);
    }
}

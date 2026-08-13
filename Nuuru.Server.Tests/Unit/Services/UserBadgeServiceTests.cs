using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.Data;
using Nuuru.Server.Models;
using Nuuru.Server.Services;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class UserBadgeServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<UserManager<ApplicationUser>> _userManager;
    private readonly Mock<RoleManager<ApplicationRole>> _roleManager;
    private readonly Mock<ISiteSettingsService> _siteSettings;
    private readonly UserBadgeService _sut;

    public UserBadgeServiceTests()
    {
        _context = TestDbContextFactory.Create();

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _userManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null, null, null, null, null, null, null, null);
        _userManager.SetupGet(x => x.Users).Returns(_context.Users);

        var roleStore = new Mock<IRoleStore<ApplicationRole>>();
        _roleManager = new Mock<RoleManager<ApplicationRole>>(
            roleStore.Object, null, null, null, null);
        _roleManager.SetupGet(x => x.Roles).Returns(_context.Roles);

        _siteSettings = new Mock<ISiteSettingsService>();
        _siteSettings
            .Setup(x => x.GetBoolAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(false);

        _sut = new UserBadgeService(
            _userManager.Object,
            _roleManager.Object,
            _context,
            _siteSettings.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task GetUserDisplayInfoAsync_WhenBabyModeEnabled_AddsBabyModeBadge()
    {
        var user = MockData.CreateTestUser();
        user.IsBabyMode = true;
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var displayInfo = await _sut.GetUserDisplayInfoAsync(user.Id);

        displayInfo.Badges.Should().Contain("babymode");
    }

    [Fact]
    public async Task GetUserDisplayInfoAsync_WhenUserRegisteredBeforeCutoff_AddsLegacyBadge()
    {
        var user = MockData.CreateTestUser();
        user.DateCreated = new DateTime(2026, 2, 23, 23, 59, 59, DateTimeKind.Utc);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var displayInfo = await _sut.GetUserDisplayInfoAsync(user.Id);

        displayInfo.Badges.Should().Contain("legacy");
    }

    [Fact]
    public async Task GetUserDisplayInfoAsync_WhenUserRegisteredOnCutoff_DoesNotAddLegacyBadge()
    {
        var user = MockData.CreateTestUser();
        user.DateCreated = new DateTime(2026, 2, 24, 0, 0, 0, DateTimeKind.Utc);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var displayInfo = await _sut.GetUserDisplayInfoAsync(user.Id);

        displayInfo.Badges.Should().NotContain("legacy");
    }
}

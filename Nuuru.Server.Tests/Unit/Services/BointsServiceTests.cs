using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.Models;
using Nuuru.Server.Services;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class BointsServiceTests
{
    private readonly Nuuru.Server.Data.ApplicationDbContext _context;
    private readonly BointsService _sut;
    private readonly Mock<ISiteSettingsService> _mockSettings;

    private static readonly Guid User1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid User2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public BointsServiceTests()
    {
        _context = TestDbContextFactory.CreateWithSeedData();
        _mockSettings = new Mock<ISiteSettingsService>();
        _mockSettings.Setup(s => s.GetBoolAsync("boints.enabled", false)).ReturnsAsync(true);
        _mockSettings.Setup(s => s.GetAsync("boints.mode")).ReturnsAsync("normal");

        _sut = new BointsService(
            _context,
            _mockSettings.Object,
            new Mock<ILogger<BointsService>>().Object);
    }

    [Fact]
    public async Task CreditAsync_IncreasesBalance()
    {
        var result = await _sut.CreditAsync(User1Id, BointsReason.Upload, 10, sourcePostId: 1);

        result.Should().BeTrue();
        var balance = await _sut.GetBalanceAsync(User1Id);
        balance.Should().Be(10);
    }

    [Fact]
    public async Task CreditAsync_DeduplicatesSameSource()
    {
        await _sut.CreditAsync(User1Id, BointsReason.Upload, 10, sourcePostId: 1);
        var result = await _sut.CreditAsync(User1Id, BointsReason.Upload, 10, sourcePostId: 1);

        result.Should().BeFalse();
        var balance = await _sut.GetBalanceAsync(User1Id);
        balance.Should().Be(10);
    }

    [Fact]
    public async Task CreditAsync_AllowsDifferentSources()
    {
        await _sut.CreditAsync(User1Id, BointsReason.ReportResolved, 10, sourcePostId: 1);
        await _sut.CreditAsync(User1Id, BointsReason.ReportResolved, 10, sourcePostId: 2);

        var balance = await _sut.GetBalanceAsync(User1Id);
        balance.Should().Be(20);
    }

    [Fact]
    public async Task CreditAsync_DeduplicatesDailyLogin()
    {
        await _sut.CreditAsync(User1Id, BointsReason.DailyLogin, 5);
        var result = await _sut.CreditAsync(User1Id, BointsReason.DailyLogin, 5);

        result.Should().BeFalse();
        var balance = await _sut.GetBalanceAsync(User1Id);
        balance.Should().Be(5);
    }

    [Fact]
    public async Task CreditAsync_DeduplicatesReactionPerUserPerComment()
    {
        await _sut.CreditAsync(User1Id, BointsReason.ReactionReceived, 1, sourceCommentId: 1, sourceUserId: User2Id);
        var result = await _sut.CreditAsync(User1Id, BointsReason.ReactionReceived, 1, sourceCommentId: 1, sourceUserId: User2Id);

        result.Should().BeFalse();
        var balance = await _sut.GetBalanceAsync(User1Id);
        balance.Should().Be(1);
    }

    [Fact]
    public async Task CreditAsync_AllowsDifferentUsersReactingSameComment()
    {
        // Need a third user for this test
        _context.Users.Add(new ApplicationUser
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333334"),
            UserName = "testuser3",
            NormalizedUserName = "TESTUSER3",
            Email = "test3@example.com",
            NormalizedEmail = "TEST3@EXAMPLE.COM",
            SecurityStamp = Guid.NewGuid().ToString(),
            Status = "active",
            Biography = "Test user 3"
        });
        await _context.SaveChangesAsync();

        await _sut.CreditAsync(User1Id, BointsReason.ReactionReceived, 1, sourceCommentId: 1, sourceUserId: User2Id);
        await _sut.CreditAsync(User1Id, BointsReason.ReactionReceived, 1, sourceCommentId: 1, sourceUserId: Guid.Parse("33333333-3333-3333-3333-333333333334"));

        var balance = await _sut.GetBalanceAsync(User1Id);
        balance.Should().Be(2);
    }

    [Fact]
    public async Task CreditAsync_WhenDisabled_ReturnsFalse()
    {
        _mockSettings.Setup(s => s.GetBoolAsync("boints.enabled", false)).ReturnsAsync(false);

        var result = await _sut.CreditAsync(User1Id, BointsReason.Upload, 10, sourcePostId: 1);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DebitAsync_DecreasesBalance()
    {
        await _sut.CreditAsync(User1Id, BointsReason.Upload, 50, sourcePostId: 1);

        var result = await _sut.DebitAsync(User1Id, 20, BointsReason.Purchase);

        result.Should().BeTrue();
        var balance = await _sut.GetBalanceAsync(User1Id);
        balance.Should().Be(30);
    }

    [Fact]
    public async Task DebitAsync_ClampsAtZero()
    {
        await _sut.CreditAsync(User1Id, BointsReason.Upload, 10, sourcePostId: 1);

        await _sut.DebitAsync(User1Id, 999, BointsReason.Purchase);

        var balance = await _sut.GetBalanceAsync(User1Id);
        balance.Should().Be(0);
    }

    [Fact]
    public async Task RevokePostCreditsAsync_RevokesAllCreditsForPost()
    {
        await _sut.CreditAsync(User1Id, BointsReason.Upload, 10, sourcePostId: 1);
        await _sut.CreditAsync(User1Id, BointsReason.FavoriteReceived, 3, sourcePostId: 1, sourceUserId: User2Id);

        await _sut.RevokePostCreditsAsync(1);

        var balance = await _sut.GetBalanceAsync(User1Id);
        balance.Should().Be(0);
    }

    [Fact]
    public async Task AdminAdjustAsync_CreditsPositiveAmount()
    {
        await _sut.AdminAdjustAsync(User1Id, 500, User2Id);

        var balance = await _sut.GetBalanceAsync(User1Id);
        balance.Should().Be(500);
    }

    [Fact]
    public async Task AdminAdjustAsync_DebitsNegativeAmount()
    {
        await _sut.AdminAdjustAsync(User1Id, 500, User2Id);
        await _sut.AdminAdjustAsync(User1Id, -200, User2Id);

        var balance = await _sut.GetBalanceAsync(User1Id);
        balance.Should().Be(300);
    }

    [Fact]
    public async Task GetLedgerAsync_ReturnsEntries()
    {
        await _sut.CreditAsync(User1Id, BointsReason.Upload, 10, sourcePostId: 1);
        await _sut.CreditAsync(User1Id, BointsReason.DailyLogin, 5);

        var (items, totalCount) = await _sut.GetLedgerAsync(User1Id, 1, 50);

        totalCount.Should().Be(2);
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreditAsync_AppliesClanTax()
    {
        // Create a clan with 10% tax
        var clan = new Clan
        {
            Name = "Test Clan",
            Tag = "TEST",
            Color = "#ff0000",
            LeaderId = User1Id,
            TaxRate = 10
        };
        _context.Clans.Add(clan);
        await _context.SaveChangesAsync();

        _context.ClanMembers.Add(new ClanMember { ClanId = clan.Id, UserId = User1Id });
        await _context.SaveChangesAsync();

        await _sut.CreditAsync(User1Id, BointsReason.Upload, 100, sourcePostId: 1);

        // User gets 90 (100 - 10% tax)
        var balance = await _sut.GetBalanceAsync(User1Id);
        balance.Should().Be(90);

        // Treasury gets 10
        var updatedClan = await _context.Clans.FindAsync(clan.Id);
        updatedClan!.Treasury.Should().Be(10);
    }

    [Fact]
    public async Task PurchaseItemAsync_DeductsBalance()
    {
        await _sut.AdminAdjustAsync(User1Id, 200, User2Id);

        var result = await _sut.PurchaseItemAsync(User1Id, "golden_frame");

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        var balance = await _sut.GetBalanceAsync(User1Id);
        balance.Should().Be(100); // 200 - 100
    }

    [Fact]
    public async Task PurchaseItemAsync_InsufficientBalance_Fails()
    {
        var result = await _sut.PurchaseItemAsync(User1Id, "profile_border");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Insufficient");
    }

    [Fact]
    public async Task PurchaseItemAsync_UnknownItem_Fails()
    {
        await _sut.AdminAdjustAsync(User1Id, 9999, User2Id);

        var result = await _sut.PurchaseItemAsync(User1Id, "nonexistent_item");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task PurchaseItemAsync_ChaosItemInNormalMode_Fails()
    {
        await _sut.AdminAdjustAsync(User1Id, 9999, User2Id);

        var result = await _sut.PurchaseItemAsync(User1Id, "rename_user", targetUserId: User2Id, content: "Loser");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("chaos");
    }

    [Fact]
    public async Task GetShopItemsAsync_NormalMode_ExcludesChaosItems()
    {
        var items = await _sut.GetShopItemsAsync();

        items.Should().NotContain(i => i.Mode == "chaos");
        items.Should().Contain(i => i.Id == "golden_frame");
    }

    [Fact]
    public async Task GetShopItemsAsync_ChaosMode_IncludesAll()
    {
        _mockSettings.Setup(s => s.GetAsync("boints.mode")).ReturnsAsync("chaos");

        var items = await _sut.GetShopItemsAsync();

        items.Should().Contain(i => i.Id == "golden_frame");
        items.Should().Contain(i => i.Id == "rename_user");
    }
}

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.DTOs.Clan;
using Nuuru.Server.Models;
using Nuuru.Server.Services;
using Nuuru.Server.Services.Storage;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class ClanServiceTests
{
    private readonly Nuuru.Server.Data.ApplicationDbContext _context;
    private readonly ClanService _sut;
    private readonly BointsService _bointsService;

    private static readonly Guid User1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid User2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public ClanServiceTests()
    {
        _context = TestDbContextFactory.CreateWithSeedData();
        var mockSettings = new Mock<ISiteSettingsService>();
        mockSettings.Setup(s => s.GetBoolAsync("boints.enabled", false)).ReturnsAsync(true);
        mockSettings.Setup(s => s.GetAsync("boints.mode")).ReturnsAsync("normal");
        var mockUserBadgeService = new Mock<IUserBadgeService>();
        mockUserBadgeService
            .Setup(s => s.GetUsersDisplayInfoAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(new Dictionary<Guid, UserDisplayInfo>());

        _bointsService = new BointsService(
            _context,
            mockSettings.Object,
            new Mock<ILogger<BointsService>>().Object);

        _sut = new ClanService(
            _context,
            _bointsService,
            mockUserBadgeService.Object,
            new Mock<IFileStorageService>().Object,
            new Mock<ILogger<ClanService>>().Object);
    }

    private async Task GiveBoints(Guid userId, int amount)
    {
        await _bointsService.AdminAdjustAsync(userId, amount, User2Id);
    }

    private void ClearTracking()
    {
        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task CreateClanAsync_WithEnoughBoints_Succeeds()
    {
        await GiveBoints(User1Id, 2000);

        var clan = await _sut.CreateClanAsync(User1Id, new CreateClanRequest
        {
            Name = "Test Clan",
            Tag = "TEST",
            Color = "#ff0000"
        });

        clan.Should().NotBeNull();
        clan!.Name.Should().Be("Test Clan");
        clan.Tag.Should().Be("TEST");
        clan.LeaderId.Should().Be(User1Id);
        clan.MemberCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateClanAsync_InsufficientBoints_ReturnsNull()
    {
        await GiveBoints(User1Id, 100);

        var clan = await _sut.CreateClanAsync(User1Id, new CreateClanRequest
        {
            Name = "Broke Clan",
            Tag = "BROKE",
            Color = "#000000"
        });

        clan.Should().BeNull();
    }

    [Fact]
    public async Task CreateClanAsync_DuplicateTag_ReturnsNull()
    {
        await GiveBoints(User1Id, 4000);

        await _sut.CreateClanAsync(User1Id, new CreateClanRequest { Name = "First", Tag = "DUPE", Color = "#ff0000" });

        // User1 is now in a clan, so use User2
        await GiveBoints(User2Id, 2000);
        var clan = await _sut.CreateClanAsync(User2Id, new CreateClanRequest { Name = "Second", Tag = "DUPE", Color = "#00ff00" });

        clan.Should().BeNull();
    }

    [Fact]
    public async Task CreateClanAsync_DeductsBoints()
    {
        await GiveBoints(User1Id, 3000);

        await _sut.CreateClanAsync(User1Id, new CreateClanRequest
        {
            Name = "Costly Clan",
            Tag = "COST",
            Color = "#ff0000"
        });

        var balance = await _bointsService.GetBalanceAsync(User1Id);
        balance.Should().Be(1000);
    }

    [Fact]
    public async Task InviteUserAsync_Succeeds()
    {
        await GiveBoints(User1Id, 2000);
        await _sut.CreateClanAsync(User1Id, new CreateClanRequest { Name = "Inv Clan", Tag = "INV", Color = "#ff0000" });

        var result = await _sut.InviteUserAsync(User1Id, 1, User2Id);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task InviteUserAsync_UserAlreadyInClan_Fails()
    {
        await GiveBoints(User1Id, 2000);
        var clan = await _sut.CreateClanAsync(User1Id, new CreateClanRequest { Name = "Full Clan", Tag = "FULL", Color = "#ff0000" });

        // User1 tries to invite themselves (already in clan)
        var result = await _sut.InviteUserAsync(User1Id, clan!.Id, User1Id);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task AcceptInviteAsync_JoinsClan()
    {
        await GiveBoints(User1Id, 2000);
        var clan = await _sut.CreateClanAsync(User1Id, new CreateClanRequest { Name = "Join Clan", Tag = "JOIN", Color = "#ff0000" });
        await _sut.InviteUserAsync(User1Id, clan!.Id, User2Id);

        var invites = await _sut.GetPendingInvitesAsync(User2Id);
        var result = await _sut.AcceptInviteAsync(User2Id, invites[0].Id);

        result.Success.Should().BeTrue();
        ClearTracking();
        var members = await _sut.GetMembersAsync(clan.Id);
        members.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeclineInviteAsync_RemovesInvite()
    {
        await GiveBoints(User1Id, 2000);
        var clan = await _sut.CreateClanAsync(User1Id, new CreateClanRequest { Name = "Dec Clan", Tag = "DEC", Color = "#ff0000" });
        await _sut.InviteUserAsync(User1Id, clan!.Id, User2Id);

        var invites = await _sut.GetPendingInvitesAsync(User2Id);
        await _sut.DeclineInviteAsync(User2Id, invites[0].Id);

        var invitesAfter = await _sut.GetPendingInvitesAsync(User2Id);
        invitesAfter.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyToClanAsync_CreatesApplication()
    {
        await GiveBoints(User1Id, 2000);
        var clan = await _sut.CreateClanAsync(User1Id, new CreateClanRequest { Name = "App Clan", Tag = "APP", Color = "#ff0000" });

        var result = await _sut.ApplyToClanAsync(User2Id, clan!.Id);

        result.Success.Should().BeTrue();
        var apps = await _sut.GetPendingApplicationsAsync(User1Id, clan.Id);
        apps.Should().HaveCount(1);
        apps[0].ApplicantUserName.Should().Be("testuser2");
    }

    [Fact]
    public async Task AcceptApplicationAsync_AddsMember()
    {
        await GiveBoints(User1Id, 2000);
        var clan = await _sut.CreateClanAsync(User1Id, new CreateClanRequest { Name = "AccApp Clan", Tag = "ACAP", Color = "#ff0000" });
        await _sut.ApplyToClanAsync(User2Id, clan!.Id);

        var apps = await _sut.GetPendingApplicationsAsync(User1Id, clan.Id);
        var result = await _sut.AcceptApplicationAsync(User1Id, clan.Id, apps[0].Id);

        result.Success.Should().BeTrue();
        ClearTracking();
        var members = await _sut.GetMembersAsync(clan.Id);
        members.Should().HaveCount(2);
    }

    [Fact]
    public async Task LeaveClanAsync_RemovesMember()
    {
        await GiveBoints(User1Id, 2000);
        var clan = await _sut.CreateClanAsync(User1Id, new CreateClanRequest { Name = "Leave Clan", Tag = "LEAV", Color = "#ff0000" });
        await _sut.InviteUserAsync(User1Id, clan!.Id, User2Id);
        var invites = await _sut.GetPendingInvitesAsync(User2Id);
        await _sut.AcceptInviteAsync(User2Id, invites[0].Id);

        var result = await _sut.LeaveClanAsync(User2Id);

        result.Success.Should().BeTrue();
        ClearTracking();
        var members = await _sut.GetMembersAsync(clan.Id);
        members.Should().HaveCount(1);
    }

    [Fact]
    public async Task LeaveClanAsync_LeaderCantLeave()
    {
        await GiveBoints(User1Id, 2000);
        await _sut.CreateClanAsync(User1Id, new CreateClanRequest { Name = "Leader Clan", Tag = "LEAD", Color = "#ff0000" });

        var result = await _sut.LeaveClanAsync(User1Id);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task KickMemberAsync_RemovesMember()
    {
        await GiveBoints(User1Id, 2000);
        var clan = await _sut.CreateClanAsync(User1Id, new CreateClanRequest { Name = "Kick Clan", Tag = "KICK", Color = "#ff0000" });
        await _sut.InviteUserAsync(User1Id, clan!.Id, User2Id);
        var invites = await _sut.GetPendingInvitesAsync(User2Id);
        await _sut.AcceptInviteAsync(User2Id, invites[0].Id);

        var result = await _sut.KickMemberAsync(User1Id, clan.Id, User2Id);

        result.Success.Should().BeTrue();
        ClearTracking();
        var members = await _sut.GetMembersAsync(clan.Id);
        members.Should().HaveCount(1);
    }

    [Fact]
    public async Task KickMemberAsync_NonLeaderCantKick()
    {
        await GiveBoints(User1Id, 2000);
        var clan = await _sut.CreateClanAsync(User1Id, new CreateClanRequest { Name = "NoKick Clan", Tag = "NOKI", Color = "#ff0000" });

        var result = await _sut.KickMemberAsync(User2Id, clan!.Id, User1Id);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserClanAsync_ReturnsClan()
    {
        await GiveBoints(User1Id, 2000);
        var clan = await _sut.CreateClanAsync(User1Id, new CreateClanRequest { Name = "My Clan", Tag = "MY", Color = "#ff0000" });

        var result = await _sut.GetUserClanAsync(User1Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(clan!.Id);
    }

    [Fact]
    public async Task GetUserClanAsync_NotInClan_ReturnsNull()
    {
        var result = await _sut.GetUserClanAsync(User2Id);

        result.Should().BeNull();
    }
}

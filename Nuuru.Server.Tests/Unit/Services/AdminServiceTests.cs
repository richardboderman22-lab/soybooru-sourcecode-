using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.Data;
using Nuuru.Server.DTOs;
using Nuuru.Server.DTOs.Admin;
using Nuuru.Server.Models;
using Nuuru.Server.Models.Booru;
using Nuuru.Server.Models.Forum;
using Nuuru.Server.Services;
using Nuuru.Server.Services.BBCode;
using Nuuru.Server.Services.Storage;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class AdminServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<ITokenService> _mockTokenService;
    private readonly Mock<IBBCodeService> _mockBBCodeService;
    private readonly Mock<IFileStorageService> _mockFileStorageService;
    private readonly Mock<ILogger<AdminService>> _mockLogger;
    private readonly AdminService _sut;

    private static readonly Guid AdminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public AdminServiceTests()
    {
        _context = TestDbContextFactory.Create();

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null, null, null, null, null, null, null, null);

        _mockTokenService = new Mock<ITokenService>();
        _mockBBCodeService = new Mock<IBBCodeService>();
        _mockFileStorageService = new Mock<IFileStorageService>();
        _mockLogger = new Mock<ILogger<AdminService>>();

        _sut = new AdminService(
            _context,
            _mockUserManager.Object,
            _mockTokenService.Object,
            _mockBBCodeService.Object,
            _mockFileStorageService.Object,
            _mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task<ApplicationUser> SeedUserAsync(Guid? id = null, string userName = "targetuser")
    {
        var user = MockData.CreateTestUser(userName, $"{userName}@example.com", id ?? UserId);
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private async Task<ApplicationUser> SeedAdminAsync()
    {
        var admin = MockData.CreateTestUser("admin", "admin@example.com", AdminId);
        await _context.Users.AddAsync(admin);
        await _context.SaveChangesAsync();
        return admin;
    }

    private async Task<ForumThread> SeedForumThreadAsync(ApplicationUser author, int threadId = 1)
    {
        var category = new ForumCategory
        {
            Id = Guid.NewGuid(),
            Slug = $"general-{threadId}",
            Name = "General",
            Description = "General discussion",
            DisplayOrder = 0,
            Color = "#ffffff"
        };

        var thread = new ForumThread
        {
            Id = threadId,
            Title = $"Thread {threadId}",
            CategoryId = category.Id,
            Category = category,
            AuthorId = author.Id,
            Author = author,
            CreatedAt = DateTime.UtcNow,
            LastPostAt = DateTime.UtcNow
        };

        await _context.ForumCategories.AddAsync(category);
        await _context.ForumThreads.AddAsync(thread);
        await _context.SaveChangesAsync();

        return thread;
    }

    // --- UpdateUserProfileAsync ---

    [Fact]
    public async Task UpdateUserProfileAsync_WithNonExistentUser_ReturnsFailure()
    {
        await SeedAdminAsync();
        var nonExistentId = Guid.NewGuid();

        var (success, error) = await _sut.UpdateUserProfileAsync(nonExistentId, "new status", null, null, AdminId);

        success.Should().BeFalse();
        error.Should().Be("User not found");
    }

    [Fact]
    public async Task UpdateUserProfileAsync_UpdatesStatus()
    {
        await SeedAdminAsync();
        var user = await SeedUserAsync();

        var (success, error) = await _sut.UpdateUserProfileAsync(user.Id, "new status", null, null, AdminId);

        success.Should().BeTrue();
        error.Should().BeNull();

        var updated = await _context.Users.FindAsync(user.Id);
        updated!.Status.Should().Be("new status");
    }

    [Fact]
    public async Task UpdateUserProfileAsync_UpdatesBiographyAndParsesHtml()
    {
        await SeedAdminAsync();
        var user = await SeedUserAsync();

        _mockBBCodeService
            .Setup(x => x.Parse("[b]hello[/b]"))
            .Returns("<b>hello</b>");

        var (success, error) = await _sut.UpdateUserProfileAsync(user.Id, null, "[b]hello[/b]", null, AdminId);

        success.Should().BeTrue();
        error.Should().BeNull();

        var updated = await _context.Users.FindAsync(user.Id);
        updated!.Biography.Should().Be("[b]hello[/b]");
        updated.BiographyHtml.Should().Be("<b>hello</b>");

        _mockBBCodeService.Verify(x => x.Parse("[b]hello[/b]"), Times.Once);
    }

    [Fact]
    public async Task UpdateUserProfileAsync_UpdatesBothStatusAndBiography()
    {
        await SeedAdminAsync();
        var user = await SeedUserAsync();

        _mockBBCodeService
            .Setup(x => x.Parse("new bio"))
            .Returns("new bio");

        var (success, error) = await _sut.UpdateUserProfileAsync(user.Id, "new status", "new bio", null, AdminId);

        success.Should().BeTrue();

        var updated = await _context.Users.FindAsync(user.Id);
        updated!.Status.Should().Be("new status");
        updated.Biography.Should().Be("new bio");
    }

    [Fact]
    public async Task UpdateUserProfileAsync_NullFieldsAreNotChanged()
    {
        await SeedAdminAsync();
        var user = await SeedUserAsync();
        var originalStatus = user.Status;
        var originalBio = user.Biography;

        var (success, _) = await _sut.UpdateUserProfileAsync(user.Id, null, null, null, AdminId);

        success.Should().BeTrue();

        var updated = await _context.Users.FindAsync(user.Id);
        updated!.Status.Should().Be(originalStatus);
        updated.Biography.Should().Be(originalBio);

        _mockBBCodeService.Verify(x => x.Parse(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateUserProfileAsync_CreatesModerationAction()
    {
        await SeedAdminAsync();
        var user = await SeedUserAsync();

        await _sut.UpdateUserProfileAsync(user.Id, "new status", null, null, AdminId);

        var action = _context.ModerationActions.FirstOrDefault(a =>
            a.Action == "UpdateProfile" && a.TargetId == user.UserName);

        action.Should().NotBeNull();
        action!.TargetType.Should().Be("User");
        action.Details.Should().Contain(user.UserName!);
    }

    [Fact]
    public async Task UpdateUserProfileAsync_AllowsEmptyStringStatus()
    {
        await SeedAdminAsync();
        var user = await SeedUserAsync();

        var (success, _) = await _sut.UpdateUserProfileAsync(user.Id, "", null, null, AdminId);

        success.Should().BeTrue();

        var updated = await _context.Users.FindAsync(user.Id);
        updated!.Status.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateUserProfileAsync_AllowsEmptyStringBiography()
    {
        await SeedAdminAsync();
        var user = await SeedUserAsync();

        _mockBBCodeService
            .Setup(x => x.Parse(""))
            .Returns("");

        var (success, _) = await _sut.UpdateUserProfileAsync(user.Id, null, "", null, AdminId);

        success.Should().BeTrue();

        var updated = await _context.Users.FindAsync(user.Id);
        updated!.Biography.Should().BeEmpty();
        updated.BiographyHtml.Should().BeEmpty();
    }

    // --- GetUserByIdAsync ---

    [Fact]
    public async Task GetUserByIdAsync_IncludesStatusAndBiography()
    {
        var user = await SeedUserAsync();
        user.Status = "test status";
        user.Biography = "test bio";
        user.IsBabyMode = true;
        await _context.SaveChangesAsync();

        _mockUserManager
            .Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string>());

        var dto = await _sut.GetUserByIdAsync(user.Id);

        dto.Should().NotBeNull();
        dto!.Status.Should().Be("test status");
        dto.Biography.Should().Be("test bio");
        dto.IsBabyMode.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateUserProfileAsync_UpdatesBabyMode()
    {
        await SeedAdminAsync();
        var user = await SeedUserAsync();

        var (success, error) = await _sut.UpdateUserProfileAsync(user.Id, null, null, true, AdminId);

        success.Should().BeTrue();
        error.Should().BeNull();

        var updated = await _context.Users.FindAsync(user.Id);
        updated!.IsBabyMode.Should().BeTrue();
    }

    [Fact]
    public async Task GetUserByIdAsync_NonExistentUser_ReturnsNull()
    {
        var dto = await _sut.GetUserByIdAsync(Guid.NewGuid());

        dto.Should().BeNull();
    }

    // --- SearchUsersAsync role filtering ---

    private async Task SeedRoleAndAssignAsync(ApplicationUser user, string roleName)
    {
        var roleId = Guid.NewGuid();
        _context.Roles.Add(new ApplicationRole
        {
            Id = roleId,
            Name = roleName,
            NormalizedName = roleName.ToUpperInvariant()
        });
        _context.UserRoles.Add(new IdentityUserRole<Guid>
        {
            UserId = user.Id,
            RoleId = roleId
        });
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchUsersAsync_WithRoleFilter_ReturnsOnlyUsersWithRole()
    {
        var user1 = await SeedUserAsync(Guid.NewGuid(), "alice");
        var user2 = await SeedUserAsync(Guid.NewGuid(), "bob");
        await SeedRoleAndAssignAsync(user1, "Moderator");

        _mockUserManager
            .Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string>());

        var result = await _sut.SearchUsersAsync(null, "Moderator", 1, 20);

        result.Items.Should().HaveCount(1);
        result.Items.First().UserName.Should().Be("alice");
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchUsersAsync_WithEmptyRole_ReturnsAllUsers()
    {
        await SeedUserAsync(Guid.NewGuid(), "alice");
        await SeedUserAsync(Guid.NewGuid(), "bob");

        _mockUserManager
            .Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string>());

        var result = await _sut.SearchUsersAsync(null, "", 1, 20);

        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchUsersAsync_WithNonExistentRole_ReturnsNone()
    {
        await SeedUserAsync(Guid.NewGuid(), "alice");

        _mockUserManager
            .Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string>());

        var result = await _sut.SearchUsersAsync(null, "NonExistentRole", 1, 20);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetActivityStatsAsync_ReturnsDailySeries_WithDistinctPosterCounts()
    {
        var userA = await SeedUserAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), "alice");
        var userB = await SeedUserAsync(Guid.Parse("22222222-2222-2222-2222-222222222222"), "bob");
        var thread = await SeedForumThreadAsync(userA, 77);

        var booruPostDayOneA = MockData.CreateTestPost(userA, 1001, "hash-day1-a");
        booruPostDayOneA.UploadedAt = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);

        var booruPostDayOneB = MockData.CreateTestPost(userB, 1002, "hash-day1-b");
        booruPostDayOneB.UploadedAt = new DateTime(2026, 4, 1, 11, 0, 0, DateTimeKind.Utc);

        var booruPostDayTwoA = MockData.CreateTestPost(userA, 1003, "hash-day2-a");
        booruPostDayTwoA.UploadedAt = new DateTime(2026, 4, 2, 9, 0, 0, DateTimeKind.Utc);

        await _context.BooruPosts.AddRangeAsync(booruPostDayOneA, booruPostDayOneB, booruPostDayTwoA);

        var forumPostDayOne = new ForumPost
        {
            Id = 2001,
            ThreadId = thread.Id,
            Thread = thread,
            AuthorId = userA.Id,
            Author = userA,
            ContentRaw = "day one forum post",
            ContentHtml = "day one forum post",
            CreatedAt = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        var forumPostDayTwoA = new ForumPost
        {
            Id = 2002,
            ThreadId = thread.Id,
            Thread = thread,
            AuthorId = userB.Id,
            Author = userB,
            ContentRaw = "day two forum post",
            ContentHtml = "day two forum post",
            CreatedAt = new DateTime(2026, 4, 2, 10, 0, 0, DateTimeKind.Utc)
        };

        var forumPostDayTwoB = new ForumPost
        {
            Id = 2003,
            ThreadId = thread.Id,
            Thread = thread,
            AuthorId = userB.Id,
            Author = userB,
            ContentRaw = "day two forum followup",
            ContentHtml = "day two forum followup",
            CreatedAt = new DateTime(2026, 4, 2, 11, 0, 0, DateTimeKind.Utc)
        };

        await _context.ForumPosts.AddRangeAsync(forumPostDayOne, forumPostDayTwoA, forumPostDayTwoB);

        var commentDayOne = MockData.CreateTestComment(booruPostDayOneA.Id, userB, "day one comment");
        commentDayOne.CreatedAt = new DateTime(2026, 4, 1, 13, 0, 0, DateTimeKind.Utc);

        var commentDayTwoA = MockData.CreateTestComment(booruPostDayTwoA.Id, userA, "day two comment");
        commentDayTwoA.CreatedAt = new DateTime(2026, 4, 2, 13, 0, 0, DateTimeKind.Utc);

        var commentDayTwoB = MockData.CreateTestComment(booruPostDayTwoA.Id, userA, "day two comment again");
        commentDayTwoB.CreatedAt = new DateTime(2026, 4, 2, 14, 0, 0, DateTimeKind.Utc);

        await _context.BooruComments.AddRangeAsync(commentDayOne, commentDayTwoA, commentDayTwoB);
        await _context.SaveChangesAsync();

        var stats = await _sut.GetActivityStatsAsync(new ActivityStatsQueryDto
        {
            DateFrom = new DateOnly(2026, 4, 1),
            DateTo = new DateOnly(2026, 4, 3)
        });

        stats.DateFrom.Should().Be(new DateOnly(2026, 4, 1));
        stats.DateTo.Should().Be(new DateOnly(2026, 4, 3));
        stats.Daily.Should().HaveCount(3);

        stats.Daily[0].Should().BeEquivalentTo(new ActivityDailyPointDto
        {
            Date = new DateOnly(2026, 4, 1),
            BooruPosts = 2,
            ForumPosts = 1,
            Comments = 1,
            PostsPerDay = 3,
            TotalActivity = 4,
            UniquePostingUsers = 2,
            UniqueActiveUsers = 2
        });

        stats.Daily[1].Should().BeEquivalentTo(new ActivityDailyPointDto
        {
            Date = new DateOnly(2026, 4, 2),
            BooruPosts = 1,
            ForumPosts = 2,
            Comments = 2,
            PostsPerDay = 3,
            TotalActivity = 5,
            UniquePostingUsers = 2,
            UniqueActiveUsers = 2
        });

        stats.Daily[2].Should().BeEquivalentTo(new ActivityDailyPointDto
        {
            Date = new DateOnly(2026, 4, 3),
            BooruPosts = 0,
            ForumPosts = 0,
            Comments = 0,
            PostsPerDay = 0,
            TotalActivity = 0,
            UniquePostingUsers = 0,
            UniqueActiveUsers = 0
        });
    }
}

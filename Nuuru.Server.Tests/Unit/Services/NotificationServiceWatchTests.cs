using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.Models;
using Nuuru.Server.Services;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class NotificationServiceWatchTests
{
    private static readonly Guid User1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid User2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid User3Id = Guid.Parse("33333333-3333-3333-3333-333333333331");

    private (NotificationService sut, Nuuru.Server.Data.ApplicationDbContext context) CreateServices()
    {
        var context = TestDbContextFactory.CreateWithSeedData();

        // Add a third user for watch tests
        context.Users.Add(new ApplicationUser
        {
            Id = User3Id,
            UserName = "testuser3",
            NormalizedUserName = "TESTUSER3",
            Email = "testuser3@example.com",
            NormalizedEmail = "TESTUSER3@EXAMPLE.COM",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            Status = "active",
            Biography = "Test user 3",
            DateCreated = DateTime.UtcNow.AddDays(-10)
        });
        context.SaveChanges();

        var watchService = new WatchService(context);
        var settingsService = new UserSettingsService(context);
        var logger = new Mock<ILogger<NotificationService>>();
        var sut = new NotificationService(context, watchService, settingsService, logger.Object);
        return (sut, context);
    }

    [Fact]
    public async Task CreateWatchedPostCommentNotifications_CreatesForWatchers()
    {
        // Arrange
        var (sut, context) = CreateServices();
        var watchService = new WatchService(context);

        // User2 and User3 watch post 1 (owned by User1)
        await watchService.ToggleWatchAsync(User2Id, WatchTargetType.BooruPost, 1);
        await watchService.ToggleWatchAsync(User3Id, WatchTargetType.BooruPost, 1);

        // Act - User3 comments on post 1
        await sut.CreateWatchedPostCommentNotificationsAsync(1, 100, User3Id);

        // Assert - User2 should get notification, User3 (author) should not, User1 (owner) should not
        var notifications = await context.Notifications
            .Where(n => n.Type == NotificationType.WatchedPostComment)
            .ToListAsync();

        notifications.Should().HaveCount(1);
        notifications[0].UserId.Should().Be(User2Id);
    }

    [Fact]
    public async Task CreateWatchedPostCommentNotifications_ExcludesCommentAuthor()
    {
        // Arrange
        var (sut, context) = CreateServices();
        var watchService = new WatchService(context);

        // User1 watches post 1 (and User1 owns it)
        await watchService.ToggleWatchAsync(User1Id, WatchTargetType.BooruPost, 1);

        // Act - User1 comments on their own post
        await sut.CreateWatchedPostCommentNotificationsAsync(1, 100, User1Id);

        // Assert - No notification (author and owner are same person)
        var notifications = await context.Notifications
            .Where(n => n.Type == NotificationType.WatchedPostComment)
            .ToListAsync();

        notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateWatchedPostCommentNotifications_DeduplicatesUnread()
    {
        // Arrange
        var (sut, context) = CreateServices();
        var watchService = new WatchService(context);

        await watchService.ToggleWatchAsync(User2Id, WatchTargetType.BooruPost, 1);

        // First comment creates notification
        await sut.CreateWatchedPostCommentNotificationsAsync(1, 100, User3Id);

        // Act - Second comment on same post while first is unread
        await sut.CreateWatchedPostCommentNotificationsAsync(1, 101, User3Id);

        // Assert - Only 1 notification (dedup)
        var notifications = await context.Notifications
            .Where(n => n.Type == NotificationType.WatchedPostComment && n.UserId == User2Id)
            .ToListAsync();

        notifications.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateWatchedPostCommentNotifications_CreatesNewAfterRead()
    {
        // Arrange
        var (sut, context) = CreateServices();
        var watchService = new WatchService(context);

        await watchService.ToggleWatchAsync(User2Id, WatchTargetType.BooruPost, 1);

        // First comment creates notification
        await sut.CreateWatchedPostCommentNotificationsAsync(1, 100, User3Id);

        // Mark it as read
        var firstNotification = await context.Notifications
            .FirstAsync(n => n.Type == NotificationType.WatchedPostComment && n.UserId == User2Id);
        firstNotification.IsRead = true;
        await context.SaveChangesAsync();

        // Act - Second comment after reading
        await sut.CreateWatchedPostCommentNotificationsAsync(1, 101, User3Id);

        // Assert - 2 notifications total (1 read + 1 new)
        var notifications = await context.Notifications
            .Where(n => n.Type == NotificationType.WatchedPostComment && n.UserId == User2Id)
            .ToListAsync();

        notifications.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateWatchedThreadReplyNotifications_CreatesForWatchers()
    {
        // Arrange
        var (sut, context) = CreateServices();
        var watchService = new WatchService(context);

        // User1 and User2 watch thread
        await watchService.ToggleWatchAsync(User1Id, WatchTargetType.ForumThread, 1);
        await watchService.ToggleWatchAsync(User2Id, WatchTargetType.ForumThread, 1);

        // Act - User3 replies
        await sut.CreateWatchedThreadReplyNotificationsAsync(1, 200, User3Id);

        // Assert - Both User1 and User2 get notifications
        var notifications = await context.Notifications
            .Where(n => n.Type == NotificationType.WatchedThreadReply)
            .ToListAsync();

        notifications.Should().HaveCount(2);
        notifications.Select(n => n.UserId).Should().Contain(User1Id);
        notifications.Select(n => n.UserId).Should().Contain(User2Id);
    }

    [Fact]
    public async Task CreateWatchedThreadReplyNotifications_ExcludesPostAuthor()
    {
        // Arrange
        var (sut, context) = CreateServices();
        var watchService = new WatchService(context);

        await watchService.ToggleWatchAsync(User1Id, WatchTargetType.ForumThread, 1);

        // Act - User1 replies (they are the watcher and author)
        await sut.CreateWatchedThreadReplyNotificationsAsync(1, 200, User1Id);

        // Assert - No notification
        var notifications = await context.Notifications
            .Where(n => n.Type == NotificationType.WatchedThreadReply)
            .ToListAsync();

        notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateWatchedThreadReplyNotifications_DeduplicatesUnread()
    {
        // Arrange
        var (sut, context) = CreateServices();
        var watchService = new WatchService(context);

        await watchService.ToggleWatchAsync(User1Id, WatchTargetType.ForumThread, 1);

        // First reply
        await sut.CreateWatchedThreadReplyNotificationsAsync(1, 200, User2Id);

        // Act - Second reply while first notification is unread
        await sut.CreateWatchedThreadReplyNotificationsAsync(1, 201, User3Id);

        // Assert - Only 1 notification
        var notifications = await context.Notifications
            .Where(n => n.Type == NotificationType.WatchedThreadReply && n.UserId == User1Id)
            .ToListAsync();

        notifications.Should().HaveCount(1);
    }
}

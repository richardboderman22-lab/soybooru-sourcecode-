using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Nuuru.Server.Auth;
using Nuuru.Server.Data;
using Nuuru.Server.Models;
using Nuuru.Server.Models.Booru;
using Nuuru.Server.Models.Forum;
using Nuuru.Server.Models.Messaging;
using Nuuru.Server.Tests.Helpers;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;

namespace Nuuru.Server.Tests.Integration.Controllers;

/// <summary>
/// Integration tests verifying that authenticated users cannot access or modify
/// other users' resources via ID manipulation (IDOR).
/// </summary>
public class IdorTests : IClassFixture<CustomWebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory<Program> _factory;
    private readonly string _dbName;

    private static readonly Guid OwnerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AttackerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private const int PostId = 1000;
    private int _commentId;
    private Guid _forumCategoryId;
    private const string ForumCategorySlug = "test";
    private const int ForumThreadId = 1000;
    private const int ForumPostOpId = 1000;
    private const int ForumPostReplyId = 1001;
    private Guid _generalCategoryId;
    private const string GeneralCategorySlug = "gen";
    private const int GeneralThreadId = 1001;
    private const int GeneralForumPostOpId = 1100;
    private const int GeneralForumPostReplyId = 1101;
    private Guid _notificationId;
    private Guid _conversationId;
    private int _messageId;
    private Guid _attachmentId;

    private const string JwtKey = "df4b2553fdea53856a0b2b9f6c3f30d8";
    private const string JwtIssuer = "Booru";
    private const string JwtAudience = "BooruAPI";

    public IdorTests(CustomWebApplicationFactory<Program> factory)
    {
        _dbName = $"nuuru_test_{Guid.NewGuid():N}";
        _factory = new CustomWebApplicationFactory<Program>();
        _factory.DatabaseName = _dbName;
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        // Seed users
        var owner = MockData.CreateTestUser("owner", "owner@example.com", OwnerId);
        var attacker = MockData.CreateTestUser("attacker", "attacker@example.com", AttackerId);
        context.Users.AddRange(owner, attacker);
        await context.SaveChangesAsync();

        // Seed post owned by Owner
        var post = MockData.CreateTestPost(owner, PostId);
        context.BooruPosts.Add(post);
        await context.SaveChangesAsync();

        // Seed comment on post by Owner
        var comment = MockData.CreateTestComment(PostId, owner, "Owner's comment");
        context.BooruComments.Add(comment);
        await context.SaveChangesAsync();
        _commentId = comment.Id;

        // Seed forum category
        var category = new ForumCategory
        {
            Id = Guid.NewGuid(),
            Slug = ForumCategorySlug,
            Name = "Test Category",
            Description = "For IDOR tests",
            Color = "#000000"
        };
        _forumCategoryId = category.Id;
        context.ForumCategories.Add(category);
        await context.SaveChangesAsync();

        var generalCategory = await context.ForumCategories.SingleAsync(c => c.Slug == GeneralCategorySlug);
        _generalCategoryId = generalCategory.Id;

        // Seed forum thread (without FirstPostId/LastPostId due to circular FK)
        var thread = new ForumThread
        {
            Id = ForumThreadId,
            Title = "Test Thread",
            CategoryId = _forumCategoryId,
            AuthorId = OwnerId
        };
        context.ForumThreads.Add(thread);
        await context.SaveChangesAsync();

        // Seed forum posts
        var opPost = new ForumPost
        {
            Id = ForumPostOpId,
            ContentRaw = "OP content",
            ContentHtml = "OP content",
            ThreadId = ForumThreadId,
            AuthorId = OwnerId
        };
        var replyPost = new ForumPost
        {
            Id = ForumPostReplyId,
            ContentRaw = "Reply content",
            ContentHtml = "Reply content",
            ThreadId = ForumThreadId,
            AuthorId = OwnerId
        };
        context.ForumPosts.AddRange(opPost, replyPost);
        await context.SaveChangesAsync();

        // Link thread to its posts
        thread.FirstPostId = ForumPostOpId;
        thread.LastPostId = ForumPostReplyId;
        await context.SaveChangesAsync();

        var generalThread = new ForumThread
        {
            Id = GeneralThreadId,
            Title = "General Test Thread",
            CategoryId = _generalCategoryId,
            AuthorId = OwnerId
        };
        context.ForumThreads.Add(generalThread);
        await context.SaveChangesAsync();

        var generalOpPost = new ForumPost
        {
            Id = GeneralForumPostOpId,
            ContentRaw = "General OP content",
            ContentHtml = "General OP content",
            ThreadId = GeneralThreadId,
            AuthorId = OwnerId
        };
        var generalReplyPost = new ForumPost
        {
            Id = GeneralForumPostReplyId,
            ContentRaw = "General reply content",
            ContentHtml = "General reply content",
            ThreadId = GeneralThreadId,
            AuthorId = OwnerId
        };
        context.ForumPosts.AddRange(generalOpPost, generalReplyPost);
        await context.SaveChangesAsync();

        generalThread.FirstPostId = GeneralForumPostOpId;
        generalThread.LastPostId = GeneralForumPostReplyId;
        await context.SaveChangesAsync();

        // Seed notification for Owner
        var notification = new Notification
        {
            Type = NotificationType.CommentOnPost,
            Message = "Test notification",
            UserId = OwnerId,
            IsRead = false
        };
        context.Notifications.Add(notification);
        await context.SaveChangesAsync();
        _notificationId = notification.Id;

        // Seed conversation with Owner as sole participant
        var conversationId = Guid.NewGuid();
        _conversationId = conversationId;

        var conversation = new Conversation
        {
            Id = conversationId,
            CreatorId = OwnerId,
            Title = "Test Conversation"
        };
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync();

        var participant = new ConversationParticipant
        {
            ConversationId = conversationId,
            UserId = OwnerId
        };
        context.ConversationParticipants.Add(participant);
        await context.SaveChangesAsync();

        // Seed message in conversation by Owner
        var message = new Message
        {
            ConversationId = conversationId,
            AuthorId = OwnerId,
            ContentRaw = "Test message",
            ContentHtml = "Test message"
        };
        context.Messages.Add(message);
        await context.SaveChangesAsync();
        _messageId = message.Id;

        // Seed forum attachment owned by Owner
        var attachment = new ForumPostAttachment
        {
            Id = Guid.NewGuid(),
            UploaderId = OwnerId,
            FileIdentifier = "test-attachment-file",
            OriginalFileName = "test.png",
            ContentType = "image/png",
            FileSize = 1024
        };
        context.ForumPostAttachments.Add(attachment);
        await context.SaveChangesAsync();
        _attachmentId = attachment.Id;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    #region Helpers

    private string GenerateJwtToken(Guid userId, string userName, params string[] permissions)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(JwtKey);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, userName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        foreach (var permission in permissions)
        {
            claims.Add(new Claim(Permissions.ClaimType, permission));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature),
            Issuer = JwtIssuer,
            Audience = JwtAudience
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private HttpClient CreateClientForOwner(params string[] permissions)
    {
        var token = GenerateJwtToken(OwnerId, "owner", permissions);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient CreateClientForAttacker(params string[] permissions)
    {
        var token = GenerateJwtToken(AttackerId, "attacker", permissions);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    #endregion

    #region Post IDOR

    [Fact]
    public async Task DeletePost_AsNonOwner_ReturnsForbidden()
    {
        var client = CreateClientForAttacker(Permissions.User.DeleteOwnContent);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/booru/posts/{PostId}");
        request.Content = JsonContent.Create(new { Reason = "attacker trying to delete" });
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeletePost_AsOwner_Succeeds()
    {
        // Seed a fresh post so this test doesn't affect others
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var owner = await context.Users.FindAsync(OwnerId);
        var freshPost = MockData.CreateTestPost(owner!, 2000, "deletehash");
        context.BooruPosts.Add(freshPost);
        await context.SaveChangesAsync();

        var client = CreateClientForOwner(Permissions.User.DeleteOwnContent);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/booru/posts/{freshPost.Id}");
        request.Content = JsonContent.Create(new { Reason = "owner deleting own post" });
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdatePostTags_AsNonOwnerWithoutEditTags_ReturnsForbidden()
    {
        // Attacker has EditOwnContent (passes [Authorize] policy) but lacks EditTags
        var client = CreateClientForAttacker(Permissions.User.EditOwnContent);

        var response = await client.PutAsJsonAsync(
            $"/api/booru/posts/{PostId}/tags",
            new { Tags = new[] { "tag1" } });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdatePostTags_AsNonOwnerWithEditTags_Succeeds()
    {
        // Non-owner with user.edit_tags can edit any post's tags (intentional bypass)
        var client = CreateClientForAttacker(Permissions.User.EditOwnContent, Permissions.User.EditTags);

        var response = await client.PutAsJsonAsync(
            $"/api/booru/posts/{PostId}/tags",
            new { Tags = new[] { "tag1" } });

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdatePostRating_AsNonOwner_ReturnsForbidden()
    {
        // Attacker has user.set_rating but not moderation.set_rating
        var client = CreateClientForAttacker(Permissions.User.SetRating);

        var response = await client.PutAsJsonAsync(
            $"/api/booru/posts/{PostId}/rating",
            new { Rating = "safe" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdatePostRating_AsNonOwnerWithModPermission_Succeeds()
    {
        var client = CreateClientForAttacker(Permissions.Moderation.SetRating);

        var response = await client.PutAsJsonAsync(
            $"/api/booru/posts/{PostId}/rating",
            new { Rating = "safe" });

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdatePostSource_AsNonOwner_ReturnsForbidden()
    {
        // Attacker has user.set_source but not moderation.set_source
        var client = CreateClientForAttacker(Permissions.User.SetSource);

        var response = await client.PutAsJsonAsync(
            $"/api/booru/posts/{PostId}/source",
            new { Source = "https://evil.example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdatePostSource_AsNonOwnerWithModPermission_Succeeds()
    {
        var client = CreateClientForAttacker(Permissions.Moderation.SetSource);

        var response = await client.PutAsJsonAsync(
            $"/api/booru/posts/{PostId}/source",
            new { Source = "https://example.com" });

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Comment IDOR

    [Fact]
    public async Task UpdateComment_AsNonOwner_ReturnsForbidden()
    {
        var client = CreateClientForAttacker(Permissions.User.EditOwnContent);

        var response = await client.PutAsJsonAsync(
            $"/api/booru/posts/{PostId}/comments/{_commentId}",
            new { Content = "hijacked comment" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateComment_AsOwner_Succeeds()
    {
        // Seed a fresh comment so this test doesn't affect shared state
        int freshCommentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var owner = await context.Users.FindAsync(OwnerId);
            var freshComment = MockData.CreateTestComment(PostId, owner!, "Original content");
            context.BooruComments.Add(freshComment);
            await context.SaveChangesAsync();
            freshCommentId = freshComment.Id;
        }

        var client = CreateClientForOwner(Permissions.User.EditOwnContent);

        var response = await client.PutAsJsonAsync(
            $"/api/booru/posts/{PostId}/comments/{freshCommentId}",
            new { Content = "Updated by owner" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteComment_AsNonOwner_ReturnsForbidden()
    {
        // Attacker has delete_own_content but no moderation.delete_comment
        var client = CreateClientForAttacker(Permissions.User.DeleteOwnContent);

        var response = await client.DeleteAsync(
            $"/api/booru/posts/{PostId}/comments/{_commentId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteComment_AsNonOwnerWithModPermission_Succeeds()
    {
        // Seed a fresh comment to delete
        int freshCommentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var owner = await context.Users.FindAsync(OwnerId);
            var freshComment = MockData.CreateTestComment(PostId, owner!, "To be deleted by mod");
            context.BooruComments.Add(freshComment);
            await context.SaveChangesAsync();
            freshCommentId = freshComment.Id;
        }

        var client = CreateClientForAttacker(
            Permissions.User.DeleteOwnContent,
            Permissions.Moderation.DeleteComment);

        var response = await client.DeleteAsync(
            $"/api/booru/posts/{PostId}/comments/{freshCommentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region Forum Post IDOR

    [Fact]
    public async Task UpdateForumPost_AsNonOwner_ReturnsForbidden()
    {
        var client = CreateClientForAttacker(Permissions.User.EditOwnContent);

        var response = await client.PutAsJsonAsync(
            $"/api/forum/threads/{ForumThreadId}/posts/{ForumPostReplyId}",
            new { Content = "hijacked forum post" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateForumPost_AsOwner_Succeeds()
    {
        // Seed a fresh forum post so this test doesn't affect shared state
        int freshPostId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var freshPost = new ForumPost
            {
                Id = 2001,
                ContentRaw = "Original content",
                ContentHtml = "Original content",
                ThreadId = ForumThreadId,
                AuthorId = OwnerId
            };
            context.ForumPosts.Add(freshPost);
            await context.SaveChangesAsync();
            freshPostId = freshPost.Id;
        }

        var client = CreateClientForOwner(Permissions.User.EditOwnContent);

        var response = await client.PutAsJsonAsync(
            $"/api/forum/threads/{ForumThreadId}/posts/{freshPostId}",
            new { Content = "Updated by owner" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteForumPost_AsNonOwner_ReturnsForbidden()
    {
        // Attacker has delete_own_content but no forum.delete_post
        // Test on the reply (not OP, since OP deletion is blocked)
        var client = CreateClientForAttacker(Permissions.User.DeleteOwnContent);

        var response = await client.DeleteAsync(
            $"/api/forum/threads/{ForumThreadId}/posts/{ForumPostReplyId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteForumPost_AsNonOwnerWithModPermission_Succeeds()
    {
        // Seed a fresh reply to delete
        int freshPostId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var freshPost = new ForumPost
            {
                Id = 2002,
                ContentRaw = "To be deleted by mod",
                ContentHtml = "To be deleted by mod",
                ThreadId = ForumThreadId,
                AuthorId = OwnerId
            };
            context.ForumPosts.Add(freshPost);
            await context.SaveChangesAsync();
            freshPostId = freshPost.Id;
        }

        var client = CreateClientForAttacker(
            Permissions.User.DeleteOwnContent,
            Permissions.Forum.DeletePost);

        var response = await client.DeleteAsync(
            $"/api/forum/threads/{ForumThreadId}/posts/{freshPostId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region Forum Thread Ban IDOR

    [Fact]
    public async Task BanThreadUser_AsNonOwner_ReturnsForbidden()
    {
        var client = CreateClientForAttacker();

        var response = await client.PostAsJsonAsync(
            $"/api/forum/categories/{ForumCategorySlug}/threads/{ForumThreadId}/bans",
            new { UserId = OwnerId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BanThreadUser_AsOwner_Succeeds()
    {
        var client = CreateClientForOwner();

        var response = await client.PostAsJsonAsync(
            $"/api/forum/categories/{ForumCategorySlug}/threads/{ForumThreadId}/bans",
            new { UserId = AttackerId });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ban = await context.ForumThreadBans.FindAsync(ForumThreadId, AttackerId);
        ban.Should().NotBeNull();
        ban!.BannedByUserId.Should().Be(OwnerId);
    }

    [Fact]
    public async Task BanThreadUser_InGeneralCategory_ReturnsBadRequest()
    {
        var client = CreateClientForOwner();

        var response = await client.PostAsJsonAsync(
            $"/api/forum/categories/{GeneralCategorySlug}/threads/{GeneralThreadId}/bans",
            new { UserId = AttackerId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateForumPost_AsBannedUser_ReturnsForbidden()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.ForumThreadBans.Add(new ForumThreadBan
            {
                ThreadId = ForumThreadId,
                UserId = AttackerId,
                BannedByUserId = OwnerId,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var client = CreateClientForAttacker(Permissions.Forum.Reply);

        var response = await client.PostAsJsonAsync(
            $"/api/forum/threads/{ForumThreadId}/posts",
            new { Content = "Trying to reply while thread-banned" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnbanThreadUser_AsOwner_AllowsReplyAgain()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.ForumThreadBans.Add(new ForumThreadBan
            {
                ThreadId = ForumThreadId,
                UserId = AttackerId,
                BannedByUserId = OwnerId,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var ownerClient = CreateClientForOwner();
        var unbanResponse = await ownerClient.DeleteAsync(
            $"/api/forum/categories/{ForumCategorySlug}/threads/{ForumThreadId}/bans/{AttackerId}");

        unbanResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var attackerClient = CreateClientForAttacker(Permissions.Forum.Reply);
        var replyResponse = await attackerClient.PostAsJsonAsync(
            $"/api/forum/threads/{ForumThreadId}/posts",
            new { Content = "Replying after unban" });

        replyResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateForumPost_InGeneralCategory_IgnoresThreadBan()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.ForumThreadBans.Add(new ForumThreadBan
            {
                ThreadId = GeneralThreadId,
                UserId = AttackerId,
                BannedByUserId = OwnerId,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var client = CreateClientForAttacker(Permissions.Forum.Reply);

        var response = await client.PostAsJsonAsync(
            $"/api/forum/threads/{GeneralThreadId}/posts",
            new { Content = "Replying in general despite dormant thread ban" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    #endregion

    #region Notification IDOR

    [Fact]
    public async Task MarkNotificationAsRead_AsNonOwner_DoesNotAffectNotification()
    {
        // Seed a fresh notification to avoid ordering issues
        Guid freshNotificationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notification = new Notification
            {
                Type = NotificationType.Mention,
                Message = "Fresh notification for mark-read test",
                UserId = OwnerId,
                IsRead = false
            };
            context.Notifications.Add(notification);
            await context.SaveChangesAsync();
            freshNotificationId = notification.Id;
        }

        var client = CreateClientForAttacker();

        // Attacker tries to mark Owner's notification as read
        var response = await client.PostAsync($"/api/notifications/{freshNotificationId}/read", null);

        // Controller returns 204 regardless (silent no-op for wrong user)
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify notification is still unread in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notification = await context.Notifications.FindAsync(freshNotificationId);
            notification.Should().NotBeNull();
            notification!.IsRead.Should().BeFalse();
        }
    }

    [Fact]
    public async Task MarkNotificationAsRead_AsOwner_MarksAsRead()
    {
        Guid freshNotificationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notification = new Notification
            {
                Type = NotificationType.Mention,
                Message = "Fresh notification for owner mark-read test",
                UserId = OwnerId,
                IsRead = false
            };
            context.Notifications.Add(notification);
            await context.SaveChangesAsync();
            freshNotificationId = notification.Id;
        }

        var client = CreateClientForOwner();

        var response = await client.PostAsync($"/api/notifications/{freshNotificationId}/read", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify notification is now read
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notification = await context.Notifications.FindAsync(freshNotificationId);
            notification.Should().NotBeNull();
            notification!.IsRead.Should().BeTrue();
        }
    }

    [Fact]
    public async Task DeleteNotification_AsNonOwner_DoesNotDeleteNotification()
    {
        Guid freshNotificationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notification = new Notification
            {
                Type = NotificationType.Mention,
                Message = "Fresh notification for delete test",
                UserId = OwnerId,
                IsRead = false
            };
            context.Notifications.Add(notification);
            await context.SaveChangesAsync();
            freshNotificationId = notification.Id;
        }

        var client = CreateClientForAttacker();

        var response = await client.DeleteAsync($"/api/notifications/{freshNotificationId}");

        // Controller returns 204 regardless (silent no-op for wrong user)
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify notification still exists
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notification = await context.Notifications.FindAsync(freshNotificationId);
            notification.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task DeleteNotification_AsOwner_DeletesNotification()
    {
        Guid freshNotificationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notification = new Notification
            {
                Type = NotificationType.Mention,
                Message = "Fresh notification for owner delete test",
                UserId = OwnerId,
                IsRead = false
            };
            context.Notifications.Add(notification);
            await context.SaveChangesAsync();
            freshNotificationId = notification.Id;
        }

        var client = CreateClientForOwner();

        var response = await client.DeleteAsync($"/api/notifications/{freshNotificationId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify notification is gone
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notification = await context.Notifications.FindAsync(freshNotificationId);
            notification.Should().BeNull();
        }
    }

    #endregion

    #region Message IDOR

    [Fact]
    public async Task GetMessages_AsNonParticipant_ReturnsNotFound()
    {
        var client = CreateClientForAttacker();

        var response = await client.GetAsync(
            $"/api/messages/conversations/{_conversationId}/messages");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMessages_AsParticipant_Succeeds()
    {
        var client = CreateClientForOwner();

        var response = await client.GetAsync(
            $"/api/messages/conversations/{_conversationId}/messages");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateMessage_AsNonAuthor_ReturnsForbidden()
    {
        // Add attacker as participant so they can reach the ownership check
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var existing = await context.ConversationParticipants
                .FirstOrDefaultAsync(p => p.ConversationId == _conversationId && p.UserId == AttackerId);
            if (existing == null)
            {
                context.ConversationParticipants.Add(new ConversationParticipant
                {
                    ConversationId = _conversationId,
                    UserId = AttackerId
                });
                await context.SaveChangesAsync();
            }
        }

        var client = CreateClientForAttacker();

        var response = await client.PutAsJsonAsync(
            $"/api/messages/conversations/{_conversationId}/messages/{_messageId}",
            new { Content = "hijacked message" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateMessage_AsAuthor_Succeeds()
    {
        // Seed a fresh message so this test doesn't affect shared state
        int freshMessageId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var message = new Message
            {
                ConversationId = _conversationId,
                AuthorId = OwnerId,
                ContentRaw = "Original message",
                ContentHtml = "Original message"
            };
            context.Messages.Add(message);
            await context.SaveChangesAsync();
            freshMessageId = message.Id;
        }

        var client = CreateClientForOwner();

        var response = await client.PutAsJsonAsync(
            $"/api/messages/conversations/{_conversationId}/messages/{freshMessageId}",
            new { Content = "Updated by author" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteMessage_AsNonAuthor_ReturnsForbidden()
    {
        // Add attacker as participant so they can reach the ownership check
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var existing = await context.ConversationParticipants
                .FirstOrDefaultAsync(p => p.ConversationId == _conversationId && p.UserId == AttackerId);
            if (existing == null)
            {
                context.ConversationParticipants.Add(new ConversationParticipant
                {
                    ConversationId = _conversationId,
                    UserId = AttackerId
                });
                await context.SaveChangesAsync();
            }
        }

        var client = CreateClientForAttacker();

        var response = await client.DeleteAsync(
            $"/api/messages/conversations/{_conversationId}/messages/{_messageId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteMessage_AsAuthor_Succeeds()
    {
        // Seed a fresh message to delete
        int freshMessageId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var message = new Message
            {
                ConversationId = _conversationId,
                AuthorId = OwnerId,
                ContentRaw = "To be deleted",
                ContentHtml = "To be deleted"
            };
            context.Messages.Add(message);
            await context.SaveChangesAsync();
            freshMessageId = message.Id;
        }

        var client = CreateClientForOwner();

        var response = await client.DeleteAsync(
            $"/api/messages/conversations/{_conversationId}/messages/{freshMessageId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region Conversation IDOR

    [Fact]
    public async Task GetConversation_AsNonParticipant_ReturnsNotFound()
    {
        var client = CreateClientForAttacker();

        var response = await client.GetAsync(
            $"/api/messages/conversations/{_conversationId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetConversation_AsParticipant_Succeeds()
    {
        var client = CreateClientForOwner();

        var response = await client.GetAsync(
            $"/api/messages/conversations/{_conversationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MarkConversationAsRead_AsNonParticipant_ReturnsNotFound()
    {
        var client = CreateClientForAttacker();

        var response = await client.PostAsync(
            $"/api/messages/conversations/{_conversationId}/read", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkConversationAsRead_AsParticipant_Succeeds()
    {
        var client = CreateClientForOwner();

        var response = await client.PostAsync(
            $"/api/messages/conversations/{_conversationId}/read", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddParticipant_AsNonParticipant_ReturnsBadRequest()
    {
        var client = CreateClientForAttacker(Permissions.Messaging.CreateGroupConversation);
        var thirdUser = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/messages/conversations/{_conversationId}/participants",
            new { UserId = thirdUser });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region SendMessage IDOR

    [Fact]
    public async Task SendMessage_AsNonParticipant_ReturnsBadRequest()
    {
        var client = CreateClientForAttacker(Permissions.Messaging.SendMessage);

        var response = await client.PostAsJsonAsync(
            $"/api/messages/conversations/{_conversationId}/messages",
            new { Content = "Trying to send into someone else's conversation" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Forum Attachment IDOR

    [Fact]
    public async Task AssociateForumAttachment_AsNonAuthor_ReturnsForbidden()
    {
        // Seed a forum post owned by Owner to associate to
        int forumPostId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var forumPost = new ForumPost
            {
                Id = 3000,
                ContentRaw = "Post for attachment test",
                ContentHtml = "Post for attachment test",
                ThreadId = ForumThreadId,
                AuthorId = OwnerId
            };
            context.ForumPosts.Add(forumPost);
            await context.SaveChangesAsync();
            forumPostId = forumPost.Id;
        }

        // Attacker tries to associate their attachment to Owner's post
        var client = CreateClientForAttacker(Permissions.Forum.UploadAttachment);

        var response = await client.PostAsJsonAsync(
            "/api/forum/attachments/associate",
            new { PostId = forumPostId, AttachmentIds = new[] { _attachmentId } });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AssociateForumAttachment_AsAuthor_Succeeds()
    {
        // Seed a fresh forum post and attachment both owned by Owner
        int forumPostId;
        Guid freshAttachmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var forumPost = new ForumPost
            {
                Id = 3001,
                ContentRaw = "Owner's post for attachment",
                ContentHtml = "Owner's post for attachment",
                ThreadId = ForumThreadId,
                AuthorId = OwnerId
            };
            context.ForumPosts.Add(forumPost);

            var attachment = new ForumPostAttachment
            {
                Id = Guid.NewGuid(),
                UploaderId = OwnerId,
                FileIdentifier = "owner-attachment-file",
                OriginalFileName = "owner.png",
                ContentType = "image/png",
                FileSize = 512
            };
            context.ForumPostAttachments.Add(attachment);
            await context.SaveChangesAsync();
            forumPostId = forumPost.Id;
            freshAttachmentId = attachment.Id;
        }

        var client = CreateClientForOwner(Permissions.Forum.UploadAttachment);

        var response = await client.PostAsJsonAsync(
            "/api/forum/attachments/associate",
            new { PostId = forumPostId, AttachmentIds = new[] { freshAttachmentId } });

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteForumAttachment_AsNonOwner_ReturnsForbidden()
    {
        var client = CreateClientForAttacker();

        var response = await client.DeleteAsync($"/api/forum/attachments/{_attachmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteForumAttachment_AsOwner_Succeeds()
    {
        // Seed a fresh attachment to delete
        Guid freshAttachmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attachment = new ForumPostAttachment
            {
                Id = Guid.NewGuid(),
                UploaderId = OwnerId,
                FileIdentifier = "to-delete-attachment",
                OriginalFileName = "delete-me.png",
                ContentType = "image/png",
                FileSize = 256
            };
            context.ForumPostAttachments.Add(attachment);
            await context.SaveChangesAsync();
            freshAttachmentId = attachment.Id;
        }

        var client = CreateClientForOwner();

        var response = await client.DeleteAsync($"/api/forum/attachments/{freshAttachmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion
}

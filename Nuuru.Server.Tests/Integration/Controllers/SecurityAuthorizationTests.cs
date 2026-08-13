using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Nuuru.Server.Auth;
using Nuuru.Server.Data;
using Nuuru.Server.Tests.Helpers;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;

namespace Nuuru.Server.Tests.Integration.Controllers;

/// <summary>
/// Verifies that every permission-protected endpoint returns 401 without auth
/// and 403 with an authenticated user who lacks the required permission.
/// Also verifies that auth-only endpoints (no specific policy) return 401.
/// </summary>
public class SecurityAuthorizationTests : IClassFixture<CustomWebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory<Program> _factory;
    private readonly string _dbName;

    private static readonly Guid TestUserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private const string JwtKey = "df4b2553fdea53856a0b2b9f6c3f30d8";
    private const string JwtIssuer = "Booru";
    private const string JwtAudience = "BooruAPI";

    public SecurityAuthorizationTests(CustomWebApplicationFactory<Program> factory)
    {
        _dbName = $"nuuru_test_{Guid.NewGuid():N}";
        _factory = factory;
        _factory.DatabaseName = _dbName;
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var user = MockData.CreateTestUser("testuser", "testuser@example.com", TestUserId);
        context.Users.Add(user);
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureDeletedAsync();
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

    private HttpClient CreateAuthenticatedClient(params string[] permissions)
    {
        var token = GenerateJwtToken(TestUserId, "testuser", permissions);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<HttpResponseMessage> SendRequestAsync(HttpClient client, string method, string url)
    {
        var httpMethod = method.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            _ => throw new ArgumentException($"Unsupported HTTP method: {method}")
        };

        var request = new HttpRequestMessage(httpMethod, url);

        if (httpMethod == HttpMethod.Post || httpMethod == HttpMethod.Put)
        {
            request.Content = JsonContent.Create(new { });
        }

        return await client.SendAsync(request);
    }

    #endregion

    #region MemberData

    private static readonly Guid PlaceholderGuid = Guid.Empty;

    public static IEnumerable<object[]> PolicyEndpoints =>
    [
        // Forum (7)
        ["POST", "/api/forum/categories/x/threads", Permissions.Forum.CreateThread],
        ["POST", "/api/forum/categories/x/threads/1/pin", Permissions.Forum.PinThread],
        ["POST", "/api/forum/categories/x/threads/1/lock", Permissions.Forum.LockThread],
        ["POST", "/api/forum/categories/x/threads/1/move", Permissions.Forum.MoveThread],
        ["DELETE", "/api/forum/categories/x/threads/1", Permissions.Forum.DeleteThread],
        ["POST", "/api/forum/threads/1/posts", Permissions.Forum.Reply],
        // POST /api/forum/attachments excluded: requires multipart form data (tested separately below)

        // User Actions (8)
        ["POST", "/api/booru/posts", Permissions.User.UploadPost],
        ["POST", "/api/booru/posts/1/comments", Permissions.User.Comment],
        ["POST", "/api/booru/posts/1/vote", Permissions.User.Vote],
        ["POST", "/api/booru/posts/1/favorite", Permissions.User.Favorite],
        ["POST", "/api/reactions/booru_post/1", Permissions.User.React],
        ["POST", "/api/moderation/reports", Permissions.User.CreateReport],
        ["GET", "/api/moderation/reports/check?targetType=post&targetId=1", Permissions.User.CreateReport],
        ["POST", "/api/messages/conversations", Permissions.Messaging.SendMessage],

        // Moderation — Post Workflow (5)
        ["GET", "/api/moderation/posts/pending", Permissions.Moderation.ApprovePost],
        ["POST", "/api/moderation/posts/1/approve", Permissions.Moderation.ApprovePost],
        ["DELETE", "/api/moderation/posts/1/reject", Permissions.Moderation.ApprovePost],
        ["POST", "/api/moderation/posts/1/lock-comments", Permissions.Moderation.LockComments],
        ["POST", "/api/moderation/posts/1/feature", Permissions.Moderation.FeaturePost],

        // Moderation — Trash (4)
        ["GET", "/api/moderation/posts/trashed", Permissions.Admin.ViewTrash],
        ["GET", "/api/moderation/posts/trashed/count", Permissions.Admin.ViewTrash],
        ["POST", "/api/moderation/posts/1/restore", Permissions.Admin.ViewTrash],
        ["DELETE", "/api/moderation/posts/1/permanent", Permissions.Admin.DeletePost],

        // Moderation — Ban Appeals (4)
        ["GET", "/api/moderation/bans/appeals?page=1", Permissions.Moderation.ReviewBanAppeals],
        ["GET", "/api/moderation/bans/appeals/pending/count", Permissions.Moderation.ReviewBanAppeals],
        ["POST", $"/api/moderation/bans/appeals/{Guid.Empty}/accept", Permissions.Moderation.ReviewBanAppeals],
        ["POST", $"/api/moderation/bans/appeals/{Guid.Empty}/reject", Permissions.Moderation.ReviewBanAppeals],

        // Moderation — Reports (5)
        ["GET", "/api/moderation/reports?page=1", Permissions.Moderation.ViewReports],
        ["GET", $"/api/moderation/reports/{Guid.Empty}", Permissions.Moderation.ViewReports],
        ["GET", "/api/moderation/reports/pending/count", Permissions.Moderation.ViewReports],
        ["POST", $"/api/moderation/reports/{Guid.Empty}/resolve", Permissions.Moderation.ViewReports],
        ["POST", $"/api/moderation/reports/{Guid.Empty}/dismiss", Permissions.Moderation.ViewReports],

        // Post History (6)
        ["POST", "/api/booru/posts/1/history/tags/1/suppress", Permissions.Moderation.SuppressHistory],
        ["DELETE", "/api/booru/posts/1/history/tags/1/suppress", Permissions.Moderation.SuppressHistory],
        ["POST", "/api/booru/posts/1/history/source/1/suppress", Permissions.Moderation.SuppressHistory],
        ["DELETE", "/api/booru/posts/1/history/source/1/suppress", Permissions.Moderation.SuppressHistory],
        ["POST", "/api/booru/posts/1/history/tags/1/revert", Permissions.User.EditTags],
        ["POST", "/api/booru/posts/1/history/source/1/revert", Permissions.User.EditTags],

        // Moderation — User Lookup (for banning)
        ["GET", "/api/moderation/users", Permissions.Moderation.BanUser],
        ["GET", $"/api/moderation/users/{Guid.Empty}", Permissions.Moderation.BanUser],

        // Admin — User Management (6)
        ["GET", "/api/admin/users", Permissions.Admin.ManageUsers],
        ["GET", $"/api/admin/users/{Guid.Empty}", Permissions.Admin.ManageUsers],
        ["POST", "/api/admin/users", Permissions.Admin.ManageUsers],
        ["POST", $"/api/admin/users/{Guid.Empty}/change-password", Permissions.Admin.ManageUsers],
        ["PUT", $"/api/admin/users/{Guid.Empty}/profile", Permissions.Admin.ManageUsers],
        ["POST", $"/api/role/{Guid.Empty}/users/{Guid.Empty}", Permissions.Admin.ManageUsers],
        ["DELETE", $"/api/role/{Guid.Empty}/users/{Guid.Empty}", Permissions.Admin.ManageUsers],

        // Admin — Site Settings (2)
        ["GET", "/api/settings/all", Permissions.Admin.SystemSettings],
        ["PUT", "/api/settings", Permissions.Admin.SystemSettings],

        // Forum Category Management (3)
        ["POST", "/api/forum/categories", Permissions.Admin.ManageCategories],
        ["PUT", $"/api/forum/categories/{Guid.Empty}", Permissions.Admin.ManageCategories],
        ["DELETE", $"/api/forum/categories/{Guid.Empty}", Permissions.Admin.ManageCategories],

        // Admin — Notification (1)
        ["POST", "/api/notifications/announcement", Permissions.Admin.SendAnnouncements],

        // Permission Management (10, class-level admin.manage_permissions)
        ["GET", $"/api/permission/user/{Guid.Empty}", Permissions.Admin.ManagePermissions],
        ["POST", $"/api/permission/user/{Guid.Empty}/grant", Permissions.Admin.ManagePermissions],
        ["POST", $"/api/permission/user/{Guid.Empty}/revoke", Permissions.Admin.ManagePermissions],
        ["PUT", $"/api/permission/user/{Guid.Empty}", Permissions.Admin.ManagePermissions],
        ["GET", "/api/permission/available", Permissions.Admin.ManagePermissions],
        ["GET", "/api/permission/permission/user.vote", Permissions.Admin.ManagePermissions],
        ["POST", $"/api/permission/user/{Guid.Empty}/deny", Permissions.Admin.ManagePermissions],
        ["POST", $"/api/permission/user/{Guid.Empty}/remove-deny", Permissions.Admin.ManagePermissions],
        ["GET", $"/api/permission/user/{Guid.Empty}/denied", Permissions.Admin.ManagePermissions],
        ["GET", $"/api/permission/user/{Guid.Empty}/effective", Permissions.Admin.ManagePermissions],

        // Role Management (8, class-level admin.manage_permissions)
        ["GET", "/api/role", Permissions.Admin.ManagePermissions],
        ["GET", $"/api/role/{Guid.Empty}", Permissions.Admin.ManagePermissions],
        ["POST", "/api/role", Permissions.Admin.ManagePermissions],
        ["PUT", $"/api/role/{Guid.Empty}", Permissions.Admin.ManagePermissions],
        ["DELETE", $"/api/role/{Guid.Empty}", Permissions.Admin.ManagePermissions],
        ["POST", $"/api/role/{Guid.Empty}/users/{Guid.Empty}", Permissions.Admin.ManagePermissions],
        ["DELETE", $"/api/role/{Guid.Empty}/users/{Guid.Empty}", Permissions.Admin.ManagePermissions],
        ["GET", $"/api/role/user/{Guid.Empty}", Permissions.Admin.ManagePermissions],

        // Audit Log (2, class-level moderation.view_audit_log)
        ["GET", "/api/audit-logs", Permissions.Moderation.ViewAuditLog],
        ["GET", "/api/audit-logs/categories", Permissions.Moderation.ViewAuditLog],

        // Messaging (1)
        ["POST", $"/api/messages/conversations/{Guid.Empty}/participants", Permissions.Messaging.CreateGroupConversation],
    ];

    public static IEnumerable<object[]> AuthOnlyEndpoints =>
    [
        ["POST", "/api/auth/revoke"],
        ["POST", "/api/auth/logout"],
        ["PUT", "/api/user/profile"],
        // POST /api/user/avatar excluded: requires multipart form data (tested separately below)
        ["DELETE", "/api/user/avatar"],
        ["GET", "/api/user/search/mention"],
        ["GET", "/api/booru/posts/1/favorite"],
        ["POST", "/api/watches/toggle"],
        ["GET", "/api/watches/status?targetType=post&targetId=1"],
        ["GET", "/api/notifications"],
        ["GET", "/api/notifications/unread-count"],
        ["POST", "/api/notifications/read-all"],
        ["POST", $"/api/notifications/{Guid.Empty}/read"],
        ["DELETE", $"/api/notifications/{Guid.Empty}"],
        ["GET", "/api/messages/conversations"],
        ["GET", "/api/messages/conversations/unread-count"],
        ["POST", "/api/messages/conversations/read-all"],
        ["GET", $"/api/messages/conversations/{Guid.Empty}"],
        ["POST", $"/api/messages/conversations/{Guid.Empty}/read"],
        ["DELETE", $"/api/messages/conversations/{Guid.Empty}/leave"],
        ["GET", $"/api/messages/conversations/{Guid.Empty}/messages"],
        ["PUT", $"/api/messages/conversations/{Guid.Empty}/messages/1"],
        ["DELETE", $"/api/messages/conversations/{Guid.Empty}/messages/1"],
        ["GET", "/api/moderation/bans/mine"],
        ["POST", "/api/moderation/bans/appeals"],
        ["GET", "/api/moderation/bans/appeals/mine"],
        ["DELETE", $"/api/forum/attachments/{Guid.Empty}"],
    ];

    #endregion

    #region Tests

    [Theory]
    [MemberData(nameof(PolicyEndpoints))]
    public async Task PolicyEndpoint_WithoutAuth_ReturnsUnauthorized(string method, string url, string permission)
    {
        var client = _factory.CreateClient();

        var response = await SendRequestAsync(client, method, url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: $"{method} {url} (requires {permission}) should reject unauthenticated requests");
    }

    [Theory]
    [MemberData(nameof(PolicyEndpoints))]
    public async Task PolicyEndpoint_WithWrongPermission_ReturnsForbidden(string method, string url, string permission)
    {
        var client = CreateAuthenticatedClient("dummy.unrelated");

        var response = await SendRequestAsync(client, method, url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: $"{method} {url} (requires {permission}) should reject users without that permission");
    }

    [Theory]
    [MemberData(nameof(AuthOnlyEndpoints))]
    public async Task AuthOnlyEndpoint_WithoutAuth_ReturnsUnauthorized(string method, string url)
    {
        var client = _factory.CreateClient();

        var response = await SendRequestAsync(client, method, url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: $"{method} {url} should reject unauthenticated requests");
    }

    // --- File upload endpoints (require multipart form data) ---

    private static HttpRequestMessage CreateMultipartPost(string url)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x89, 0x50 }), "file", "test.png");
        return new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
    }

    [Fact]
    public async Task ForumAttachmentUpload_WithoutAuth_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.SendAsync(CreateMultipartPost("/api/forum/attachments"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ForumAttachmentUpload_WithWrongPermission_ReturnsForbidden()
    {
        var client = CreateAuthenticatedClient("dummy.unrelated");
        var response = await client.SendAsync(CreateMultipartPost("/api/forum/attachments"));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UserAvatarUpload_WithoutAuth_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.SendAsync(CreateMultipartPost("/api/user/avatar"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion
}

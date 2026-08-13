// ----------------------------------------------------------------------------
// Nuuru.Server.Tests - ModerationControllerTests
// ----------------------------------------------------------------------------
// Integration tests for ModerationController authorization and access control.
//
// These tests verify that moderation endpoints are properly secured with:
// - JWT-based authentication (401 Unauthorized for unauthenticated requests)
// - Policy-based authorization (403 Forbidden without required permissions)
// - Correct access control for each permission level
//
// Test Structure:
// - Each endpoint has 3 test scenarios: unauthenticated, unauthorized, and authorized
// - Tests focus on authorization, not full CRUD functionality
// - Uses real JWT tokens with permission claims
// ----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Nuuru.Server.Auth;
using Nuuru.Server.Data;
using Nuuru.Server.Models;
using Nuuru.Server.Models.Booru;
using Nuuru.Server.Services.Storage;
using Nuuru.Server.Tests.Helpers;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;

namespace Nuuru.Server.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the ModerationController API endpoints.
/// Validates authentication and authorization requirements for all moderation actions.
/// </summary>
/// <remarks>
/// <para>
/// This test class verifies that moderation endpoints are properly protected by:
/// <list type="bullet">
/// <item><description>JWT authentication - Returns 401 for unauthenticated requests</description></item>
/// <item><description>Permission-based authorization - Returns 403 without required permissions</description></item>
/// <item><description>Allows access only when user has the specific permission claim</description></item>
/// </list>
/// </para>
/// <para>
/// Each test uses a unique PostgreSQL database to ensure isolation.
/// The database is created in InitializeAsync and dropped in DisposeAsync.
/// </para>
/// </remarks>
public class ModerationControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory<Program> _factory;
    private readonly string _dbName;
    private readonly Guid _testUserId = Guid.NewGuid();

    // JWT Configuration (matches appsettings.json)
    private const string JwtKey = "df4b2553fdea53856a0b2b9f6c3f30d8";
    private const string JwtIssuer = "Booru";
    private const string JwtAudience = "BooruAPI";

    public ModerationControllerTests(CustomWebApplicationFactory<Program> factory)
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

        // Create test user for authenticated requests (required for FK constraints)
        var testUser = MockData.CreateTestUser("testuser", "testuser@example.com", _testUserId);
        context.Users.Add(testUser);
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureDeletedAsync();
    }

    #region Helper Methods

    /// <summary>
    /// Generates a JWT token with the specified user ID, username, and permission claims.
    /// </summary>
    /// <param name="userId">The unique identifier for the user.</param>
    /// <param name="userName">The username to include in the token.</param>
    /// <param name="permissions">Variable number of permission claim values (e.g., "moderation.trash_post").</param>
    /// <returns>A JWT token string that can be used in the Authorization header.</returns>
    /// <remarks>
    /// The token is configured to match the application's JWT settings (key, issuer, audience).
    /// Permission claims are added with the claim type defined in <see cref="Permissions.ClaimType"/>.
    /// The token expires after 1 hour.
    /// </remarks>
    private string GenerateJwtToken(Guid userId, string userName, params string[] permissions)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(JwtKey);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, userName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        // Add permission claims
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

    /// <summary>
    /// Creates an HTTP client configured with JWT Bearer authentication.
    /// </summary>
    /// <param name="permissions">Permission claims to include in the JWT token.</param>
    /// <returns>An HttpClient with the Authorization header set to a Bearer token.</returns>
    /// <remarks>
    /// This helper method simplifies creating authenticated clients for testing.
    /// A random user ID is generated for each client to ensure test isolation.
    /// </remarks>
    private HttpClient CreateAuthenticatedClient(params string[] permissions)
    {
        var token = GenerateJwtToken(_testUserId, "testuser", permissions);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    #endregion

    #region TrashPost Tests

    /// <summary>
    /// Verifies that attempting to trash a post without authentication returns 401 Unauthorized.
    /// </summary>
    /// <remarks>
    /// This test ensures that the [Authorize] attribute is properly applied to the TrashPost endpoint.
    /// Unauthenticated requests should be rejected before reaching authorization checks.
    /// </remarks>
    [Fact]
    public async Task TrashPost_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.DeleteAsync("/api/moderation/posts/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TrashPost_WithoutRequiredPermission_ReturnsForbidden()
    {
        // Arrange - User authenticated but lacks moderation.trash_post permission
        var client = CreateAuthenticatedClient(Permissions.User.UploadPost);

        // Act
        var response = await client.DeleteAsync("/api/moderation/posts/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies that a user with the correct permission can access the trash post endpoint.
    /// </summary>
    /// <remarks>
    /// This test confirms that authorization passes when the user has the
    /// <see cref="Permissions.Moderation.TrashPost"/> permission claim.
    /// The test focuses on authorization (no 401/403), not the full trash operation.
    /// The endpoint may return 404 if the post doesn't exist, which is acceptable for this test.
    /// </remarks>
    [Fact]
    public async Task TrashPost_WithRequiredPermission_DoesNotReturnUnauthorizedOrForbidden()
    {
        // Arrange - Authenticated user with correct permission
        var client = CreateAuthenticatedClient(Permissions.Moderation.TrashPost);

        // Act - Try to delete a post (doesn't matter if it exists for authorization test)
        var response = await client.DeleteAsync("/api/moderation/posts/1");

        // Assert - Should not be 401 or 403 (authorization passed)
        // May be 404 (not found) or other codes, but not auth failures
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    #endregion

    #region DeleteComment Tests

    [Fact]
    public async Task DeleteComment_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var commentId = 99999; // Using int since Comment.Id is int

        // Act
        var response = await client.DeleteAsync($"/api/moderation/comments/{commentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteComment_WithoutRequiredPermission_ReturnsForbidden()
    {
        // Arrange - User has different permission
        var client = CreateAuthenticatedClient(Permissions.Moderation.TrashPost);
        var commentId = 99999; // Using int since Comment.Id is int

        // Act
        var response = await client.DeleteAsync($"/api/moderation/comments/{commentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteComment_WithRequiredPermission_DoesNotReturnUnauthorizedOrForbidden()
    {
        // Arrange - Authenticated user with correct permission
        var client = CreateAuthenticatedClient(Permissions.Moderation.DeleteComment);

        // Act - Try to delete a comment (doesn't matter if it exists for authorization test)
        var response = await client.DeleteAsync($"/api/moderation/comments/{99999}");

        // Assert - Should not be 401 or 403 (authorization passed)
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    #endregion

    #region EditPostTags Tests

    [Fact]
    public async Task EditPostTags_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new { Tags = new[] { "tag1", "tag2" } };

        // Act
        var response = await client.PutAsJsonAsync("/api/moderation/posts/1/tags", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EditPostTags_WithoutRequiredPermission_ReturnsForbidden()
    {
        // Arrange - User has upload permission but not moderation.edit_tags
        var client = CreateAuthenticatedClient(Permissions.User.EditTags);
        var request = new { Tags = new[] { "tag1", "tag2" } };

        // Act
        var response = await client.PutAsJsonAsync("/api/moderation/posts/1/tags", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EditPostTags_WithRequiredPermission_DoesNotReturnUnauthorizedOrForbidden()
    {
        // Arrange - Authenticated user with correct permission
        var client = CreateAuthenticatedClient(Permissions.Moderation.EditTags);
        var request = new { Tags = new[] { "tag1", "tag2" } };

        // Act - Try to edit tags (doesn't matter if post exists for authorization test)
        var response = await client.PutAsJsonAsync("/api/moderation/posts/1/tags", request);

        // Assert - Should not be 401 or 403 (authorization passed)
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    #endregion

    #region BanUser Tests

    [Fact]
    public async Task BanUser_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new
        {
            UserId = Guid.NewGuid(),
            Reason = "Test ban",
            Zone = BanZone.Sitewide
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/moderation/users/ban", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BanUser_WithoutRequiredPermission_ReturnsForbidden()
    {
        // Arrange - User has view audit log but not ban permission
        var client = CreateAuthenticatedClient(Permissions.Moderation.ViewAuditLog);
        var request = new
        {
            UserId = Guid.NewGuid(),
            Reason = "Test ban",
            Zone = BanZone.Sitewide
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/moderation/users/ban", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BanUser_WithRequiredPermission_DoesNotReturnUnauthorizedOrForbidden()
    {
        // Arrange - Authenticated user with correct permission
        var client = CreateAuthenticatedClient(Permissions.Moderation.BanUser);
        var request = new
        {
            UserId = Guid.NewGuid(),
            Reason = "Test ban",
            Zone = BanZone.Sitewide
        };

        // Act - Try to ban a user (doesn't matter if user exists for authorization test)
        var response = await client.PostAsJsonAsync("/api/moderation/users/ban", request);

        // Assert - Should not be 401 or 403 (authorization passed)
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    #endregion

    #region UnbanUser Tests

    [Fact]
    public async Task UnbanUser_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new
        {
            UserId = Guid.NewGuid(),
            Zone = (BanZone?)BanZone.Sitewide
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/moderation/users/unban", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnbanUser_WithoutRequiredPermission_ReturnsForbidden()
    {
        // Arrange - User has trash post but not ban permission
        var client = CreateAuthenticatedClient(Permissions.Moderation.TrashPost);
        var request = new
        {
            UserId = Guid.NewGuid(),
            Zone = (BanZone?)BanZone.Sitewide
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/moderation/users/unban", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnbanUser_WithRequiredPermission_DoesNotReturnUnauthorizedOrForbidden()
    {
        // Arrange - Authenticated user with correct permission
        var client = CreateAuthenticatedClient(Permissions.Moderation.BanUser);
        var request = new
        {
            UserId = Guid.NewGuid(),
            Zone = (BanZone?)BanZone.Sitewide
        };

        // Act - Try to unban a user (doesn't matter if user exists for authorization test)
        var response = await client.PostAsJsonAsync("/api/moderation/users/unban", request);

        // Assert - Should not be 401 or 403 (authorization passed)
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    #endregion

    #region ViewAuditLog Tests

    [Fact]
    public async Task GetModerationLogs_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/moderation/logs?page=1&pageSize=50");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetModerationLogs_WithoutRequiredPermission_ReturnsForbidden()
    {
        // Arrange - User has ban permission but not view audit log
        var client = CreateAuthenticatedClient(Permissions.Moderation.BanUser);

        // Act
        var response = await client.GetAsync("/api/moderation/logs?page=1&pageSize=50");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetModerationLogs_WithRequiredPermission_ReturnsOk()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Moderation.ViewAuditLog);

        // Act
        var response = await client.GetAsync("/api/moderation/logs?page=1&pageSize=50");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUserModerationLogs_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/moderation/logs/user/{userId}?page=1&pageSize=50");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUserModerationLogs_WithoutRequiredPermission_ReturnsForbidden()
    {
        // Arrange - User has delete comment but not view audit log
        var client = CreateAuthenticatedClient(Permissions.Moderation.DeleteComment);
        var userId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/moderation/logs/user/{userId}?page=1&pageSize=50");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUserModerationLogs_WithRequiredPermission_ReturnsOk()
    {
        // Arrange - Seed test user
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = MockData.CreateTestUser("targetuser", "targetuser@example.com");
        await context.Users.AddAsync(user);
        await context.SaveChangesAsync();

        var client = CreateAuthenticatedClient(Permissions.Moderation.ViewAuditLog);

        // Act
        var response = await client.GetAsync($"/api/moderation/logs/user/{user.Id}?page=1&pageSize=50");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Multiple Permissions Tests

    /// <summary>
    /// Verifies that a user with all moderation permissions can access all moderation endpoints.
    /// </summary>
    /// <remarks>
    /// This comprehensive test validates that a moderator with full permissions
    /// can successfully call all moderation endpoints without receiving authorization errors.
    /// This simulates a super-moderator or admin role with complete moderation capabilities.
    /// </remarks>
    [Fact]
    public async Task ModerationEndpoints_WithAllPermissions_AllSucceed()
    {
        // Arrange - User with all moderation permissions
        var client = CreateAuthenticatedClient(
            Permissions.Moderation.TrashPost,
            Permissions.Moderation.DeleteComment,
            Permissions.Moderation.EditTags,
            Permissions.Moderation.BanUser,
            Permissions.Moderation.BanIp,
            Permissions.Moderation.ViewAuditLog
        );

        // Seed test data (reuse the seeded test user from InitializeAsync)
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await context.Users.FindAsync(_testUserId);
        var post = MockData.CreateTestPost(user!, 99);

        await context.BooruPosts.AddAsync(post);
        await context.SaveChangesAsync();

        // Act & Assert - All endpoints should be accessible
        var logsResponse = await client.GetAsync("/api/moderation/logs?page=1&pageSize=50");
        logsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var userLogsResponse = await client.GetAsync($"/api/moderation/logs/user/{user.Id}?page=1&pageSize=50");
        userLogsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var ipBansResponse = await client.GetAsync("/api/moderation/ip-bans?page=1&pageSize=20");
        ipBansResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that a regular user without any moderation permissions cannot access moderation endpoints.
    /// </summary>
    /// <remarks>
    /// This test ensures that moderation endpoints are properly locked down.
    /// A user authenticated with only regular user permissions (e.g., upload posts)
    /// should receive 403 Forbidden on all moderation endpoints.
    /// This is a critical security test to prevent privilege escalation.
    /// </remarks>
    [Fact]
    public async Task ModerationEndpoints_WithNoPermissions_AllReturnForbidden()
    {
        // Arrange - Authenticated user with no moderation permissions
        var client = CreateAuthenticatedClient(Permissions.User.UploadPost);

        // Act & Assert - All endpoints should return 403
        var deletePostResponse = await client.DeleteAsync("/api/moderation/posts/1");
        deletePostResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deleteCommentResponse = await client.DeleteAsync($"/api/moderation/comments/{99999}");
        deleteCommentResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var editTagsResponse = await client.PutAsJsonAsync("/api/moderation/posts/1/tags",
            new { Tags = new[] { "tag1" } });
        editTagsResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var banResponse = await client.PostAsJsonAsync("/api/moderation/users/ban",
            new { UserId = Guid.NewGuid(), Reason = "Test", Zone = BanZone.Sitewide });
        banResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var logsResponse = await client.GetAsync("/api/moderation/logs?page=1&pageSize=50");
        logsResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ipBansResponse = await client.GetAsync("/api/moderation/ip-bans?page=1&pageSize=20");
        ipBansResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var createIpBanResponse = await client.PostAsJsonAsync("/api/moderation/ip-bans",
            new { IpAddress = "192.168.1.1", Reason = "Test" });
        createIpBanResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region IP Ban Tests

    [Fact]
    public async Task CreateIpBan_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new { IpAddress = "192.168.1.1", Reason = "Test" };

        // Act
        var response = await client.PostAsJsonAsync("/api/moderation/ip-bans", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateIpBan_WithoutRequiredPermission_ReturnsForbidden()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Moderation.BanUser);
        var request = new { IpAddress = "192.168.1.1", Reason = "Test" };

        // Act
        var response = await client.PostAsJsonAsync("/api/moderation/ip-bans", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateIpBan_WithRequiredPermission_DoesNotReturnUnauthorizedOrForbidden()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Moderation.BanIp);
        var request = new { IpAddress = "192.168.1.1", Reason = "Test" };

        // Act
        var response = await client.PostAsJsonAsync("/api/moderation/ip-bans", request);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemoveIpBan_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.DeleteAsync($"/api/moderation/ip-bans/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RemoveIpBan_WithoutRequiredPermission_ReturnsForbidden()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Moderation.BanUser);

        // Act
        var response = await client.DeleteAsync($"/api/moderation/ip-bans/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemoveIpBan_WithRequiredPermission_DoesNotReturnUnauthorizedOrForbidden()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Moderation.BanIp);

        // Act
        var response = await client.DeleteAsync($"/api/moderation/ip-bans/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetActiveIpBans_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/moderation/ip-bans?page=1&pageSize=20");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetActiveIpBans_WithoutRequiredPermission_ReturnsForbidden()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Moderation.BanUser);

        // Act
        var response = await client.GetAsync("/api/moderation/ip-bans?page=1&pageSize=20");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetActiveIpBans_WithRequiredPermission_ReturnsOk()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Moderation.BanIp);

        // Act
        var response = await client.GetAsync("/api/moderation/ip-bans?page=1&pageSize=20");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CheckIpBan_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/moderation/ip-bans/check?ip=192.168.1.1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CheckIpBan_WithoutRequiredPermission_ReturnsForbidden()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Moderation.BanUser);

        // Act
        var response = await client.GetAsync("/api/moderation/ip-bans/check?ip=192.168.1.1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CheckIpBan_WithRequiredPermission_DoesNotReturnUnauthorizedOrForbidden()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Moderation.BanIp);

        // Act
        var response = await client.GetAsync("/api/moderation/ip-bans/check?ip=192.168.1.1");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Trashed Media Signed URL Tests

    [Fact]
    public async Task GetTrashedMediaUrls_WithViewTrashPermission_ReturnsUsableSignedUrls()
    {
        // Arrange
        const int postId = 7401;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

            var uploader = MockData.CreateTestUser("trasheduploader", "trasheduploader@example.com");
            await context.Users.AddAsync(uploader);

            await using var fileStream = new MemoryStream([0xFF, 0xD8, 0xFF, 0xD9]);
            var fileResult = await fileStorage.SaveFileAsync(
                fileStream,
                "trashed-main.jpg",
                new FileStorageOptions
                {
                    ContentType = "image/jpeg",
                    UploaderId = uploader.Id
                });

            await using var thumbStream = new MemoryStream([0x89, 0x50, 0x4E, 0x47]);
            var thumbResult = await fileStorage.SaveFileAsync(
                thumbStream,
                "trashed-thumb.png",
                new FileStorageOptions
                {
                    ContentType = "image/png",
                    UploaderId = uploader.Id
                });

            var post = new Post
            {
                Id = postId,
                StorageIdentifier = fileResult.FileIdentifier!,
                ImageHash = $"hash_{postId}",
                MimeType = "image/jpeg",
                FileSize = 4,
                OriginalFileName = "trashed-main.jpg",
                Width = 1200,
                Height = 800,
                UploadedAt = DateTime.UtcNow,
                IsApproved = true,
                IsTrashed = true,
                TrashedAt = DateTime.UtcNow,
                ThumbnailPath = thumbResult.FileIdentifier,
                Uploader = uploader,
                PostTags = [],
                Comments = []
            };

            await context.BooruPosts.AddAsync(post);
            await context.SaveChangesAsync();
        }

        var privilegedClient = CreateAuthenticatedClient(Permissions.Admin.ViewTrash);
        var anonymousClient = _factory.CreateClient();

        // Act
        var mediaUrlsResponse = await privilegedClient.GetAsync($"/api/moderation/posts/{postId}/media-urls?expiresInSeconds=300");
        var mediaUrls = await mediaUrlsResponse.Content.ReadFromJsonAsync<TrashedMediaUrlsResponse>();
        var unsignedFileResponse = await anonymousClient.GetAsync($"/api/booru/posts/{postId}/file");
        var signedFileResponse = await anonymousClient.GetAsync(mediaUrls!.FileUrl);
        var signedThumbResponse = await anonymousClient.GetAsync(mediaUrls!.ThumbnailUrl!);

        // Assert
        mediaUrlsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        mediaUrls.Should().NotBeNull();
        mediaUrls!.FileUrl.Should().Contain($"/api/booru/posts/{postId}/file");
        mediaUrls.ThumbnailUrl.Should().Contain($"/api/booru/posts/{postId}/thumbnail");

        unsignedFileResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        signedFileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        signedThumbResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    private sealed class TrashedMediaUrlsResponse
    {
        public string FileUrl { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}

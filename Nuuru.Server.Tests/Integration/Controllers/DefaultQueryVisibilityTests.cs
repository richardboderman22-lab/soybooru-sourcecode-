using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Nuuru.Server.Auth;
using Nuuru.Server.Data;
using Nuuru.Server.DTOs;
using Nuuru.Server.DTOs.Booru;
using Nuuru.Server.Models;
using Nuuru.Server.Models.Booru;
using Nuuru.Server.Tests.Helpers;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;

namespace Nuuru.Server.Tests.Integration.Controllers;

public class DefaultQueryVisibilityTests : IClassFixture<CustomWebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory<Program> _factory;
    private readonly string _dbName;

    private static readonly Guid TestUserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private const int SafePostId = 7001;
    private const int HiddenPostId = 7002;

    private const string JwtKey = "df4b2553fdea53856a0b2b9f6c3f30d8";
    private const string JwtIssuer = "Booru";
    private const string JwtAudience = "BooruAPI";

    public DefaultQueryVisibilityTests(CustomWebApplicationFactory<Program> factory)
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

        var user = MockData.CreateTestUser("visibility-user", "visibility@example.com", TestUserId);
        var safePost = MockData.CreateTestPost(user, SafePostId, "safehash");
        var hiddenPost = MockData.CreateTestPost(user, HiddenPostId, "hiddenhash");
        hiddenPost.Rating = PostRating.Explicit;

        context.Users.Add(user);
        context.BooruPosts.AddRange(safePost, hiddenPost);
        context.UserSettings.Add(new UserSettings
        {
            UserId = TestUserId,
            DefaultSearchQuery = "-rating:explicit"
        });

        context.BooruComments.Add(MockData.CreateTestComment(SafePostId, user, "Visible comment"));
        context.BooruComments.Add(MockData.CreateTestComment(SafePostId, user, $"[thumb]{HiddenPostId}[/thumb]"));

        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task GetPostById_WhenAnonymousDefaultQueryHidesPost_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/booru/posts/{HiddenPostId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdatePostTags_WhenAuthenticatedDefaultQueryHidesPost_ReturnsNotFound()
    {
        var client = CreateAuthenticatedClient(Permissions.User.EditOwnContent);

        var response = await client.PutAsJsonAsync(
            $"/api/booru/posts/{HiddenPostId}/tags",
            new { Tags = new[] { "test_tag" } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetComments_OmitsCommentsContainingHiddenThumbs()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/booru/posts/{SafePostId}/comments");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<CommentDto>>();
        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle();
        result.Items.Single().ContentHtml.Should().Be("Visible comment");
    }

    private HttpClient CreateAuthenticatedClient(params string[] permissions)
    {
        var token = GenerateJwtToken(TestUserId, "visibility-user", permissions);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string GenerateJwtToken(Guid userId, string userName, params string[] permissions)
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
}

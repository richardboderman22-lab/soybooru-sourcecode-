using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Nuuru.Server.Data;
using Nuuru.Server.Tests.Helpers;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;

namespace Nuuru.Server.Tests.Integration.Controllers;

/// <summary>
/// Verifies that the JWT middleware properly rejects malformed and invalid tokens.
/// Uses GET /api/notifications as the probe endpoint (requires [Authorize]).
/// </summary>
public class JwtSecurityTests : IClassFixture<CustomWebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory<Program> _factory;
    private readonly string _dbName;

    private const string ProbeUrl = "/api/notifications";
    private static readonly Guid TestUserId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private const string JwtKey = "df4b2553fdea53856a0b2b9f6c3f30d8";
    private const string JwtIssuer = "Booru";
    private const string JwtAudience = "BooruAPI";

    public JwtSecurityTests(CustomWebApplicationFactory<Program> factory)
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
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureDeletedAsync();
    }

    #region Helpers

    private string GenerateToken(string signingKey, DateTime? expires = null, DateTime? notBefore = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(signingKey);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, TestUserId.ToString()),
            new(ClaimTypes.Name, "testuser"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Sub, TestUserId.ToString()),
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = notBefore ?? DateTime.UtcNow,
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature),
            Issuer = JwtIssuer,
            Audience = JwtAudience
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    #endregion

    [Fact]
    public async Task ExpiredToken_ReturnsUnauthorized()
    {
        var token = GenerateToken(JwtKey, expires: DateTime.UtcNow.AddHours(-1), notBefore: DateTime.UtcNow.AddHours(-2));
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(ProbeUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidSignature_ReturnsUnauthorized()
    {
        var wrongKey = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"; // 32-char key, different from real one
        var token = GenerateToken(wrongKey);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(ProbeUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MalformedToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var response = await client.GetAsync(ProbeUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EmptyBearerToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer ");

        var response = await client.GetAsync(ProbeUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MissingBearerScheme_ReturnsUnauthorized()
    {
        var token = GenerateToken(JwtKey);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", token);

        var response = await client.GetAsync(ProbeUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

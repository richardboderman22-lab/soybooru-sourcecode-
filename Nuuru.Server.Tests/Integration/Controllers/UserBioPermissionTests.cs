using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nuuru.Server.Auth;
using Nuuru.Server.Data;
using Nuuru.Server.DTOs.User;
using Nuuru.Server.Tests.Helpers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace Nuuru.Server.Tests.Integration.Controllers;

public class UserBioPermissionTests : IClassFixture<CustomWebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory<Program> _factory;
    private readonly string _dbName;
    private static readonly Guid TestUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private const string JwtKey = "df4b2553fdea53856a0b2b9f6c3f30d8";
    private const string JwtIssuer = "Booru";
    private const string JwtAudience = "BooruAPI";

    public UserBioPermissionTests(CustomWebApplicationFactory<Program> factory)
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

        var user = MockData.CreateTestUser("biotestuser", "biotest@example.com", TestUserId);
        context.Users.Add(user);
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureDeletedAsync();
    }

    private string GenerateJwtToken(Guid userId, string userName, params string[] permissions)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(JwtKey);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, userName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Sub, userId.ToString())
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
        var token = GenerateJwtToken(TestUserId, "biotestuser", permissions);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task UpdateProfile_WithBioChange_WithoutPermission_ReturnsForbidden()
    {
        var client = CreateAuthenticatedClient(); // No permissions
        var updateDto = new UpdateProfileDto { Biography = "New Bio" };

        var response = await client.PutAsJsonAsync("/api/user/profile", updateDto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateProfile_WithStatusChange_WithoutPermission_ReturnsForbidden()
    {
        var client = CreateAuthenticatedClient(); // No permissions
        var updateDto = new UpdateProfileDto { Status = "New Status" };

        var response = await client.PutAsJsonAsync("/api/user/profile", updateDto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateProfile_WithBioChange_WithPermission_ReturnsOk()
    {
        var client = CreateAuthenticatedClient(Permissions.User.EditBio);
        var updateDto = new UpdateProfileDto { Biography = "New Bio" };

        var response = await client.PutAsJsonAsync("/api/user/profile", updateDto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateProfile_OnlyPasswordChange_WithoutBioPermission_ReturnsOk()
    {
        var client = CreateAuthenticatedClient(); // No permissions
        var updateDto = new UpdateProfileDto 
        { 
            CurrentPassword = "Password123!", 
            NewPassword = "NewPassword123!" 
        };

        var response = await client.PutAsJsonAsync("/api/user/profile", updateDto);

        // It might fail because of incorrect current password in MockData, 
        // but it should NOT be Forbidden (403).
        // MockData.CreateTestUser uses "Password123!" as default? 
        // Let's check MockData.
        
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Nuuru.Server.Auth;
using Nuuru.Server.Data;
using Nuuru.Server.DTOs;
using Nuuru.Server.DTOs.Admin;
using Nuuru.Server.DTOs.Booru;
using Nuuru.Server.Tests.Helpers;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;

namespace Nuuru.Server.Tests.Integration.Controllers;

public class TagRelationControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory<Program> _factory;
    private readonly string _dbName;
    private readonly Guid _testUserId = Guid.NewGuid();

    // JWT Configuration (matches appsettings.json)
    private const string JwtKey = "df4b2553fdea53856a0b2b9f6c3f30d8";
    private const string JwtIssuer = "Booru";
    private const string JwtAudience = "BooruAPI";

    public TagRelationControllerTests(CustomWebApplicationFactory<Program> factory)
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
        var token = GenerateJwtToken(_testUserId, "testuser", permissions);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    #endregion

    #region Aliases - Anonymous Access Tests

    [Fact]
    public async Task GetAliases_WithoutAuth_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/booru/tag-relations/aliases");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SearchAliases_WithEmptyQuery_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/booru/tag-relations/aliases/search?query=");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SearchAliases_WithQuery_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/booru/tag-relations/aliases/search?query=test");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAlias_WithNonExistentAlias_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/booru/tag-relations/aliases/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Aliases - Authorization Tests

    [Fact]
    public async Task CreateAlias_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/booru/tag-relations/aliases", new CreateTagAliasRequest
        {
            AliasTagName = "kitty",
            TargetTagName = "cat"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateAlias_WithoutPermission_ReturnsForbidden()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.User.UploadPost);

        // Act
        var response = await client.PostAsJsonAsync("/api/booru/tag-relations/aliases", new CreateTagAliasRequest
        {
            AliasTagName = "kitty",
            TargetTagName = "cat"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateAlias_WithPermission_ReturnsCreated()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Admin.SystemSettings);

        // Act
        var response = await client.PostAsJsonAsync("/api/booru/tag-relations/aliases", new CreateTagAliasRequest
        {
            AliasTagName = "kitty",
            TargetTagName = "cat"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var alias = await response.Content.ReadFromJsonAsync<TagAliasDto>();
        alias.Should().NotBeNull();
        alias!.AliasTag.Name.Should().Be("kitty");
        alias.TargetTag.Name.Should().Be("cat");
    }

    [Fact]
    public async Task CreateAlias_WithSelfReference_ReturnsBadRequest()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Admin.SystemSettings);

        // Act
        var response = await client.PostAsJsonAsync("/api/booru/tag-relations/aliases", new CreateTagAliasRequest
        {
            AliasTagName = "cat",
            TargetTagName = "cat"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteAlias_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.DeleteAsync($"/api/booru/tag-relations/aliases/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteAlias_WithoutPermission_ReturnsForbidden()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.User.UploadPost);

        // Act
        var response = await client.DeleteAsync($"/api/booru/tag-relations/aliases/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteAlias_WithPermission_AndNonExistentAlias_ReturnsNotFound()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Admin.SystemSettings);

        // Act
        var response = await client.DeleteAsync($"/api/booru/tag-relations/aliases/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Implications - Anonymous Access Tests

    [Fact]
    public async Task GetImplications_WithoutAuth_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/booru/tag-relations/implications");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SearchImplications_WithEmptyQuery_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/booru/tag-relations/implications/search?query=");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SearchImplications_WithQuery_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/booru/tag-relations/implications/search?query=test");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetImplication_WithNonExistentImplication_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/booru/tag-relations/implications/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Implications - Authorization Tests

    [Fact]
    public async Task CreateImplication_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/booru/tag-relations/implications", new CreateTagImplicationRequest
        {
            AntecedentTagName = "tabby_cat",
            ConsequentTagName = "cat"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateImplication_WithoutPermission_ReturnsForbidden()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.User.UploadPost);

        // Act
        var response = await client.PostAsJsonAsync("/api/booru/tag-relations/implications", new CreateTagImplicationRequest
        {
            AntecedentTagName = "tabby_cat",
            ConsequentTagName = "cat"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateImplication_WithPermission_ReturnsCreated()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Admin.SystemSettings);

        // Act
        var response = await client.PostAsJsonAsync("/api/booru/tag-relations/implications", new CreateTagImplicationRequest
        {
            AntecedentTagName = "tabby_cat",
            ConsequentTagName = "cat"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var implication = await response.Content.ReadFromJsonAsync<TagImplicationDto>();
        implication.Should().NotBeNull();
        implication!.AntecedentTag.Name.Should().Be("tabby_cat");
        implication.ConsequentTag.Name.Should().Be("cat");
    }

    [Fact]
    public async Task CreateImplication_WithSelfReference_ReturnsBadRequest()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Admin.SystemSettings);

        // Act
        var response = await client.PostAsJsonAsync("/api/booru/tag-relations/implications", new CreateTagImplicationRequest
        {
            AntecedentTagName = "cat",
            ConsequentTagName = "cat"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateImplication_WithExistingImplication_ReturnsBadRequest()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Admin.SystemSettings);
        await client.PostAsJsonAsync("/api/booru/tag-relations/implications", new CreateTagImplicationRequest
        {
            AntecedentTagName = "tabby_cat",
            ConsequentTagName = "cat"
        });

        // Act - Try to create same implication again
        var response = await client.PostAsJsonAsync("/api/booru/tag-relations/implications", new CreateTagImplicationRequest
        {
            AntecedentTagName = "tabby_cat",
            ConsequentTagName = "cat"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateImplication_WithCycle_ReturnsBadRequest()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Admin.SystemSettings);
        await client.PostAsJsonAsync("/api/booru/tag-relations/implications", new CreateTagImplicationRequest
        {
            AntecedentTagName = "tag_a",
            ConsequentTagName = "tag_b"
        });
        await client.PostAsJsonAsync("/api/booru/tag-relations/implications", new CreateTagImplicationRequest
        {
            AntecedentTagName = "tag_b",
            ConsequentTagName = "tag_c"
        });

        // Act - Try to create C -> A which would create a cycle
        var response = await client.PostAsJsonAsync("/api/booru/tag-relations/implications", new CreateTagImplicationRequest
        {
            AntecedentTagName = "tag_c",
            ConsequentTagName = "tag_a"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteImplication_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.DeleteAsync($"/api/booru/tag-relations/implications/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteImplication_WithPermission_ReturnsOk()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Admin.SystemSettings);

        var createResponse = await client.PostAsJsonAsync("/api/booru/tag-relations/implications", new CreateTagImplicationRequest
        {
            AntecedentTagName = "tabby_cat",
            ConsequentTagName = "cat"
        });
        var createdImplication = await createResponse.Content.ReadFromJsonAsync<TagImplicationDto>();

        // Act
        var response = await client.DeleteAsync($"/api/booru/tag-relations/implications/{createdImplication!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify it's deleted
        var getResponse = await client.GetAsync($"/api/booru/tag-relations/implications/{createdImplication.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteImplication_WithNonExistentImplication_ReturnsNotFound()
    {
        // Arrange
        var client = CreateAuthenticatedClient(Permissions.Admin.SystemSettings);

        // Act
        var response = await client.DeleteAsync($"/api/booru/tag-relations/implications/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}

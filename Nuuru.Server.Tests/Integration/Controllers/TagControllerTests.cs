using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nuuru.Server.Data;
using Nuuru.Server.DTOs.Booru;
using Nuuru.Server.Tests.Helpers;
using System.Net;
using System.Net.Http.Json;

namespace Nuuru.Server.Tests.Integration.Controllers;

public class TagControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _dbName;

    public TagControllerTests(CustomWebApplicationFactory<Program> factory)
    {
        _dbName = $"nuuru_test_{Guid.NewGuid():N}";
        _factory = factory;
        _factory.DatabaseName = _dbName;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        // Create unique database for this test class
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        // Clean up unique database after tests complete
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task GetAllTags_WithoutAuth_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/booru/tags");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tags = await response.Content.ReadFromJsonAsync<List<TagDto>>();
        tags.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAllTags_ReturnsTagsOrderedByPostCount()
    {
        // Arrange - Seed test data
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.BooruTags.AddRangeAsync(
            MockData.CreateTestTag("unpopular", postCount: 5),
            MockData.CreateTestTag("popular", postCount: 100),
            MockData.CreateTestTag("moderate", postCount: 50)
        );
        await context.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/booru/tags");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tags = await response.Content.ReadFromJsonAsync<List<TagDto>>();
        tags.Should().NotBeNull();
        tags!.Should().HaveCountGreaterThanOrEqualTo(3);

        // First tag should be the most popular
        var popularTag = tags.FirstOrDefault(t => t.Name == "popular");
        var moderateTag = tags.FirstOrDefault(t => t.Name == "moderate");
        var unpopularTag = tags.FirstOrDefault(t => t.Name == "unpopular");

        if (popularTag != null && moderateTag != null && unpopularTag != null)
        {
            tags.IndexOf(popularTag).Should().BeLessThan(tags.IndexOf(moderateTag));
            tags.IndexOf(moderateTag).Should().BeLessThan(tags.IndexOf(unpopularTag));
        }
    }

    [Fact]
    public async Task GetTagById_WithExistingTag_ReturnsTag()
    {
        // Arrange - Seed test data
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tagId = Guid.NewGuid();
        var tag = MockData.CreateTestTag("test-tag", tagId);
        await context.BooruTags.AddAsync(tag);
        await context.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/booru/tags/{tagId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tagDto = await response.Content.ReadFromJsonAsync<TagDto>();
        tagDto.Should().NotBeNull();
        tagDto!.Id.Should().Be(tagId);
        tagDto.Name.Should().Be("test-tag");
    }

    [Fact]
    public async Task GetTagById_WithNonExistentTag_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/booru/tags/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTagByName_WithExistingTag_ReturnsTag()
    {
        // Arrange - Seed test data
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tag = MockData.CreateTestTag("landscape");
        await context.BooruTags.AddAsync(tag);
        await context.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/booru/tags/name/landscape");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tagDto = await response.Content.ReadFromJsonAsync<TagDto>();
        tagDto.Should().NotBeNull();
        tagDto!.Name.Should().Be("landscape");
    }

    [Fact]
    public async Task GetTagByName_IsCaseInsensitive()
    {
        // Arrange - Seed test data
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tag = MockData.CreateTestTag("landscape");
        await context.BooruTags.AddAsync(tag);
        await context.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/booru/tags/name/LANDSCAPE");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tagDto = await response.Content.ReadFromJsonAsync<TagDto>();
        tagDto.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTagByName_WithNonExistentTag_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/booru/tags/name/nonexistent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPopularTags_WithValidCount_ReturnsPopularTags()
    {
        // Arrange - Seed test data
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        for (int i = 1; i <= 30; i++)
        {
            await context.BooruTags.AddAsync(MockData.CreateTestTag($"tag{i}", postCount: i));
        }
        await context.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/booru/tags/popular?count=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tags = await response.Content.ReadFromJsonAsync<List<TagDto>>();
        tags.Should().NotBeNull();
        tags!.Should().HaveCount(10);
    }

    [Fact]
    public async Task GetPopularTags_WithInvalidCount_ReturnsBadRequest()
    {
        // Arrange - count > 100
        var response1 = await _client.GetAsync("/api/booru/tags/popular?count=101");

        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Arrange - count < 1
        var response2 = await _client.GetAsync("/api/booru/tags/popular?count=0");

        // Assert
        response2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SearchTags_WithValidQuery_ReturnsMatchingTags()
    {
        // Arrange - Seed test data
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.BooruTags.AddRangeAsync(
            MockData.CreateTestTag("landscape", postCount: 1),
            MockData.CreateTestTag("portrait", postCount: 1),
            MockData.CreateTestTag("land", postCount: 1),
            MockData.CreateTestTag("abstract", postCount: 1)
        );
        await context.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/booru/tags/search?query=land&limit=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tags = await response.Content.ReadFromJsonAsync<List<TagDto>>();
        tags.Should().NotBeNull();
        tags!.Should().HaveCountGreaterThanOrEqualTo(2);
        tags.Should().Contain(t => t.Name == "landscape");
        tags.Should().Contain(t => t.Name == "land");
    }

    [Fact]
    public async Task SearchTags_WithEmptyQuery_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync("/api/booru/tags/search?query=&limit=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SearchTags_WithInvalidLimit_ReturnsBadRequest()
    {
        // Arrange - limit > 100
        var response1 = await _client.GetAsync("/api/booru/tags/search?query=test&limit=101");

        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Arrange - limit < 1
        var response2 = await _client.GetAsync("/api/booru/tags/search?query=test&limit=0");

        // Assert
        response2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SearchTags_RespectsLimit()
    {
        // Arrange - Seed test data
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        for (int i = 1; i <= 20; i++)
        {
            await context.BooruTags.AddAsync(MockData.CreateTestTag($"searchtag{i}", postCount: i));
        }
        await context.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/booru/tags/search?query=searchtag&limit=5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tags = await response.Content.ReadFromJsonAsync<List<TagDto>>();
        tags.Should().NotBeNull();
        tags!.Should().HaveCount(5);
    }
}

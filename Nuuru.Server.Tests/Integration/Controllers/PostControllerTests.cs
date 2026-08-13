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

// Response type for the search endpoint
public class PostSearchResponse
{
    public List<PostDto> Posts { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class PostControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _dbName;

    public PostControllerTests(CustomWebApplicationFactory<Program> factory)
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
    public async Task GetPosts_WithoutAuth_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/booru/posts?page=1&pageSize=20");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PostSearchResponse>();
        result.Should().NotBeNull();
        result!.Posts.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPosts_WithInvalidPage_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync("/api/booru/posts?page=0&pageSize=20");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetPosts_WithInvalidPageSize_ReturnsBadRequest()
    {
        // Arrange - pageSize > 100
        var response1 = await _client.GetAsync("/api/booru/posts?page=1&pageSize=101");

        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Arrange - pageSize < 1
        var response2 = await _client.GetAsync("/api/booru/posts?page=1&pageSize=0");

        // Assert
        response2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetPosts_WithValidPagination_ReturnsPaginatedResults()
    {
        // Arrange - Seed test data
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = MockData.CreateTestUser();
        await context.Users.AddAsync(user);

        for (int i = 1; i <= 25; i++)
        {
            var post = MockData.CreateTestPost(user, i, $"hash{i}");
            await context.BooruPosts.AddAsync(post);
        }
        await context.SaveChangesAsync();

        // Clear search cache so seeded posts are found
        var cache = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        if (cache is Microsoft.Extensions.Caching.Memory.MemoryCache mc)
            mc.Compact(1.0);

        // Act - Get first page
        var responsePage1 = await _client.GetAsync("/api/booru/posts?page=1&pageSize=10");
        var responsePage2 = await _client.GetAsync("/api/booru/posts?page=2&pageSize=10");

        // Assert
        responsePage1.StatusCode.Should().Be(HttpStatusCode.OK);
        var result1 = await responsePage1.Content.ReadFromJsonAsync<PostSearchResponse>();
        result1!.Posts.Should().HaveCount(10);
        result1.TotalCount.Should().Be(25);
        result1.TotalPages.Should().Be(3);

        responsePage2.StatusCode.Should().Be(HttpStatusCode.OK);
        var result2 = await responsePage2.Content.ReadFromJsonAsync<PostSearchResponse>();
        result2!.Posts.Should().HaveCount(10);

        // Ensure different posts on different pages
        result1.Posts.Select(p => p.Id).Should().NotIntersectWith(result2.Posts.Select(p => p.Id));
    }

    [Fact]
    public async Task GetPostById_WithExistingPost_ReturnsPost()
    {
        // Arrange - Seed test data
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = MockData.CreateTestUser();
        var post = MockData.CreateTestPost(user, 100);
        await context.Users.AddAsync(user);
        await context.BooruPosts.AddAsync(post);
        await context.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/booru/posts/100");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var postDto = await response.Content.ReadFromJsonAsync<PostDto>();
        postDto.Should().NotBeNull();
        postDto!.Id.Should().Be(100);
    }

    [Fact]
    public async Task GetPostById_WithNonExistentPost_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/booru/posts/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UploadPost_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var content = new MultipartFormDataContent();
        var fileBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        content.Add(new ByteArrayContent(fileBytes), "file", "test.png");

        // Act
        var response = await _client.PostAsync("/api/booru/posts", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeletePost_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange - Seed test data
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = MockData.CreateTestUser();
        var post = MockData.CreateTestPost(user, 200);
        await context.Users.AddAsync(user);
        await context.BooruPosts.AddAsync(post);
        await context.SaveChangesAsync();

        // Act
        var response = await _client.DeleteAsync("/api/booru/posts/200");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdatePostTags_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange - Seed test data
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = MockData.CreateTestUser();
        var post = MockData.CreateTestPost(user, 300);
        await context.Users.AddAsync(user);
        await context.BooruPosts.AddAsync(post);
        await context.SaveChangesAsync();

        var request = new { Tags = new[] { "tag1", "tag2" } };

        // Act
        var response = await _client.PutAsJsonAsync("/api/booru/posts/300/tags", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.Data;
using Nuuru.Server.Models.Booru;
using Nuuru.Server.Services;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class TagServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ILogger<TagService>> _mockLogger;
    private readonly TagService _sut;

    public TagServiceTests()
    {
        _context = TestDbContextFactory.Create();
        _mockLogger = new Mock<ILogger<TagService>>();
        _sut = new TagService(_context, _mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task GetTagByNameAsync_WithExistingTag_ReturnsTag()
    {
        // Arrange
        var tag = MockData.CreateTestTag("landscape");
        await _context.BooruTags.AddAsync(tag);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetTagByNameAsync("landscape");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("landscape");
    }

    [Fact]
    public async Task GetTagByNameAsync_IsCaseInsensitive()
    {
        // Arrange
        var tag = MockData.CreateTestTag("landscape");
        await _context.BooruTags.AddAsync(tag);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetTagByNameAsync("LANDSCAPE");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("landscape");
    }

    [Fact]
    public async Task GetTagByNameAsync_WithNonExistentTag_ReturnsNull()
    {
        // Act
        var result = await _sut.GetTagByNameAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTagByIdAsync_WithExistingTag_ReturnsTag()
    {
        // Arrange
        var tagId = Guid.NewGuid();
        var tag = MockData.CreateTestTag("portrait", tagId);
        await _context.BooruTags.AddAsync(tag);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetTagByIdAsync(tagId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(tagId);
    }

    [Fact]
    public async Task GetTagByIdAsync_WithNonExistentTag_ReturnsNull()
    {
        // Act
        var result = await _sut.GetTagByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateTagAsync_WithExistingTag_ReturnsExistingTag()
    {
        // Arrange - Create tag without a category to match GetOrCreateTagAsync lookup
        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            Name = "nature",
            Category = null,
            PostCount = 0
        };
        await _context.BooruTags.AddAsync(tag);
        await _context.SaveChangesAsync();
        var initialCount = await _context.BooruTags.CountAsync();

        // Act
        var result = await _sut.GetOrCreateTagAsync("nature");

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(tag.Id);

        // Verify no new tag was created
        var finalCount = await _context.BooruTags.CountAsync();
        finalCount.Should().Be(initialCount);
    }

    [Fact]
    public async Task GetOrCreateTagAsync_WithNewTag_CreatesAndReturnsTag()
    {
        // Act
        var result = await _sut.GetOrCreateTagAsync("newtag");

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("newtag");
        result.PostCount.Should().Be(0);

        // Verify tag was added to database
        var tagInDb = await _context.BooruTags.FindAsync(result.Id);
        tagInDb.Should().NotBeNull();
    }

    [Fact]
    public async Task GetOrCreateTagAsync_NormalizesTagName()
    {
        // Act
        var result = await _sut.GetOrCreateTagAsync("  MixedCase  ");

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("mixedcase");
    }

    [Fact]
    public async Task GetPopularTagsAsync_ReturnsTagsOrderedByPostCount()
    {
        // Arrange
        await _context.BooruTags.AddRangeAsync(
            MockData.CreateTestTag("unpopular", postCount: 5),
            MockData.CreateTestTag("popular", postCount: 100),
            MockData.CreateTestTag("moderate", postCount: 50)
        );
        await _context.SaveChangesAsync();

        // Act
        var results = (await _sut.GetPopularTagsAsync(10)).ToList();

        // Assert
        results.Should().HaveCount(3);
        results[0].Name.Should().Be("popular");
        results[1].Name.Should().Be("moderate");
        results[2].Name.Should().Be("unpopular");
    }

    [Fact]
    public async Task GetPopularTagsAsync_RespectsCountLimit()
    {
        // Arrange
        for (int i = 1; i <= 30; i++)
        {
            await _context.BooruTags.AddAsync(MockData.CreateTestTag($"tag{i}", postCount: i));
        }
        await _context.SaveChangesAsync();

        // Act
        var results = await _sut.GetPopularTagsAsync(10);

        // Assert
        results.Count().Should().Be(10);
    }

    [Fact]
    public async Task SearchTagsAsync_FindsTagsContainingQuery()
    {
        // Arrange
        await _context.BooruTags.AddRangeAsync(
            MockData.CreateTestTag("landscape", postCount: 1),
            MockData.CreateTestTag("portrait", postCount: 1),
            MockData.CreateTestTag("abstract", postCount: 1),
            MockData.CreateTestTag("land", postCount: 1)
        );
        await _context.SaveChangesAsync();

        // Act
        var results = (await _sut.SearchTagsAsync("land", limit: 10)).ToList();

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain(t => t.Name == "landscape");
        results.Should().Contain(t => t.Name == "land");
    }

    [Fact]
    public async Task SearchTagsAsync_IsCaseInsensitive()
    {
        // Arrange
        await _context.BooruTags.AddAsync(MockData.CreateTestTag("landscape", postCount: 1));
        await _context.SaveChangesAsync();

        // Act
        var results = await _sut.SearchTagsAsync("LAND");

        // Assert
        results.Should().HaveCount(1);
        results.First().Name.Should().Be("landscape");
    }

    [Fact]
    public async Task SearchTagsAsync_RespectsLimit()
    {
        // Arrange
        for (int i = 1; i <= 20; i++)
        {
            await _context.BooruTags.AddAsync(MockData.CreateTestTag($"tag{i}", postCount: i));
        }
        await _context.SaveChangesAsync();

        // Act
        var results = await _sut.SearchTagsAsync("tag", limit: 5);

        // Assert
        results.Count().Should().Be(5);
    }

    [Fact]
    public async Task UpdatePostCountAsync_UpdatesCountCorrectly()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        await _context.Users.AddAsync(user);

        var tag = MockData.CreateTestTag("test-tag", postCount: 0);
        await _context.BooruTags.AddAsync(tag);

        var post1 = MockData.CreateTestPost(user, 1);
        var post2 = MockData.CreateTestPost(user, 2, "hash2");
        await _context.BooruPosts.AddRangeAsync(post1, post2);

        await _context.Set<PostTag>().AddRangeAsync(
            MockData.CreatePostTag(post1.Id, tag.Id),
            MockData.CreatePostTag(post2.Id, tag.Id)
        );

        await _context.SaveChangesAsync();

        // Act
        await _sut.UpdatePostCountAsync(tag.Id);

        // Assert
        var updatedTag = await _context.BooruTags.FindAsync(tag.Id);
        updatedTag!.PostCount.Should().Be(2);
    }

    [Fact]
    public async Task UpdatePostCountAsync_WithNoAssociatedPosts_DeletesOrphanedTag()
    {
        // Arrange - Create tag without a category to avoid FK issues
        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            Name = "lonely-tag",
            Category = null,
            PostCount = 5
        };
        await _context.BooruTags.AddAsync(tag);
        await _context.SaveChangesAsync();

        // Act
        await _sut.UpdatePostCountAsync(tag.Id);

        // Assert - Tag should be deleted since it has no posts
        var deletedTag = await _context.BooruTags.FindAsync(tag.Id);
        deletedTag.Should().BeNull();
    }

    [Fact]
    public async Task UpdatePostTagsAsync_RemovesNonLockedTags()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        await _context.Users.AddAsync(user);

        var post = MockData.CreateTestPost(user, 1);
        await _context.BooruPosts.AddAsync(post);

        var tag1 = MockData.CreateTestTag("old-tag");
        var tag2 = MockData.CreateTestTag("locked-tag");
        await _context.BooruTags.AddRangeAsync(tag1, tag2);

        var postTag1 = MockData.CreatePostTag(post.Id, tag1.Id);
        postTag1.IsLocked = false;

        var postTag2 = MockData.CreatePostTag(post.Id, tag2.Id);
        postTag2.IsLocked = true;

        post.PostTags = new List<PostTag> { postTag1, postTag2 };
        await _context.SaveChangesAsync();

        // Act
        await _sut.UpdatePostTagsAsync(post, new[] { "new-tag" });

        // Assert
        await _context.Entry(post).Collection(p => p.PostTags).LoadAsync();

        post.PostTags.Should().HaveCount(2);
        post.PostTags.Should().Contain(pt => pt.TagId == tag2.Id); // Locked tag should remain
        post.PostTags.Should().NotContain(pt => pt.TagId == tag1.Id); // Non-locked tag should be removed
    }

    [Fact]
    public async Task UpdatePostTagsAsync_AddsNewTags()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        await _context.Users.AddAsync(user);

        var post = MockData.CreateTestPost(user, 1);
        post.PostTags = new List<PostTag>();
        await _context.BooruPosts.AddAsync(post);
        await _context.SaveChangesAsync();

        // Act
        await _sut.UpdatePostTagsAsync(post, new[] { "tag1", "tag2", "tag3" });

        // Assert
        await _context.Entry(post).Collection(p => p.PostTags).LoadAsync();
        post.PostTags.Should().HaveCount(3);

        // Verify tags were created
        var tags = await _context.BooruTags.ToListAsync();
        tags.Should().Contain(t => t.Name == "tag1");
        tags.Should().Contain(t => t.Name == "tag2");
        tags.Should().Contain(t => t.Name == "tag3");
    }

    [Fact]
    public async Task GetAllTagsAsync_ReturnsAllTagsOrderedByPostCount()
    {
        // Arrange
        await _context.BooruTags.AddRangeAsync(
            MockData.CreateTestTag("tag1", postCount: 10),
            MockData.CreateTestTag("tag2", postCount: 50),
            MockData.CreateTestTag("tag3", postCount: 5)
        );
        await _context.SaveChangesAsync();

        // Act
        var results = (await _sut.GetAllTagsAsync()).ToList();

        // Assert
        results.Should().HaveCount(3);
        results[0].PostCount.Should().Be(50);
        results[1].PostCount.Should().Be(10);
        results[2].PostCount.Should().Be(5);
    }

    [Fact]
    public void ValidateTagsForCategory_Artworks_RequiresMediaTag()
    {
        // Arrange
        var tags = new[] { "character:test", "artist:me" };
        var category = PostCategory.Artworks;

        // Act
        var result = _sut.ValidateTagsForCategory(tags, category);

        // Assert
        result.Should().Be("Artworks gallery requires at least one media: tag (e.g., media:ongezellig, media:original_content)");
    }

    [Fact]
    public void ValidateTagsForCategory_Artworks_DisallowsVariantOrNasTags()
    {
        // Arrange
        var tagsWithVariant = new[] { "media:original_content", "variant:soyak" };
        var tagsWithNas = new[] { "media:original_content", "nas:pepe" };
        var category = PostCategory.Artworks;

        // Act
        var resultVariant = _sut.ValidateTagsForCategory(tagsWithVariant, category);
        var resultNas = _sut.ValidateTagsForCategory(tagsWithNas, category);

        // Assert
        resultVariant.Should().Be("Artworks gallery cannot have variant: or nas: tags");
        resultNas.Should().Be("Artworks gallery cannot have variant: or nas: tags");
    }

    [Fact]
    public void ValidateTagsForCategory_Artworks_ValidTags_ReturnsNull()
    {
        // Arrange
        var tags = new[] { "media:original_content", "character:test" };
        var category = PostCategory.Artworks;

        // Act
        var result = _sut.ValidateTagsForCategory(tags, category);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateTagsForCategory_Gallery_RequiresVariantOrNasTag()
    {
        // Arrange
        var tags = new[] { "character:test", "media:original_content" };
        var category = PostCategory.Gallery;

        // Act
        var result = _sut.ValidateTagsForCategory(tags, category);

        // Assert
        result.Should().Be("Gallery requires at least one variant: or nas: tag (e.g., variant:soyak, nas:pepe)");
    }

    [Fact]
    public void ValidateTagsForCategory_Gallery_ValidWithVariant_ReturnsNull()
    {
        // Arrange
        var tags = new[] { "variant:soyak", "character:test" };
        var category = PostCategory.Gallery;

        // Act
        var result = _sut.ValidateTagsForCategory(tags, category);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateTagsForCategory_Gallery_ValidWithNas_ReturnsNull()
    {
        // Arrange
        var tags = new[] { "nas:pepe", "character:test" };
        var category = PostCategory.Gallery;

        // Act
        var result = _sut.ValidateTagsForCategory(tags, category);

        // Assert
        result.Should().BeNull();
    }
}

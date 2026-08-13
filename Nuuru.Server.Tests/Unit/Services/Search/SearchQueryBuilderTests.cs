using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nuuru.Server.Data;
using Nuuru.Server.Models;
using Nuuru.Server.Models.Booru;
using Nuuru.Server.Services;
using Nuuru.Server.Services.Search;
using Nuuru.Server.Services.Search.Nodes;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services.Search;

/// <summary>
/// Integration tests for SearchQueryBuilder using InMemory database.
/// These tests verify false positives (returning posts that shouldn't match)
/// and false negatives (missing posts that should match).
/// </summary>
public class SearchQueryBuilderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly SearchQueryBuilder _builder;
    private readonly ApplicationUser _user;

    public SearchQueryBuilderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
        // Use SystemUserContext so all posts are visible (ignoring approval filtering for these tests)
        _builder = new SearchQueryBuilder(_context, new SystemUserContext());
        _user = MockData.CreateTestUser();
        _context.Users.Add(_user);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    #region Helper Methods

    private Post CreatePost(int id, params string[] tagNames)
    {
        var post = MockData.CreateTestPost(_user, id, $"hash{id}");
        _context.BooruPosts.Add(post);
        _context.SaveChanges();

        foreach (var tagName in tagNames)
        {
            var normalizedName = tagName.ToLowerInvariant();
            var tag = _context.BooruTags.FirstOrDefault(t => t.Name == normalizedName && t.Category == null);
            if (tag == null)
            {
                tag = new Tag { Name = normalizedName, PostCount = 0 };
                _context.BooruTags.Add(tag);
            }

            tag.PostCount++;
            _context.SaveChanges();

            var postTag = new PostTag
            {
                PostId = post.Id,
                TagId = tag.Id,
                AddedAt = DateTime.UtcNow
            };
            _context.Set<PostTag>().Add(postTag);
        }

        post.TagCount = tagNames.Length;
        _context.SaveChanges();
        return post;
    }

    private Post CreatePostWithCategory(int id, string categorySlug, string tagName)
    {
        var post = MockData.CreateTestPost(_user, id, $"hash{id}");
        _context.BooruPosts.Add(post);
        _context.SaveChanges();

        var normalizedSlug = categorySlug.ToLowerInvariant();
        var category = _context.BooruTagCategories.FirstOrDefault(c => c.Slug == normalizedSlug);
        if (category == null)
        {
            category = new TagCategory
            {
                Name = categorySlug.ToUpperInvariant(),
                Slug = normalizedSlug,
                ColorHex = "#ffffff",
                IsActive = true
            };
            _context.BooruTagCategories.Add(category);
            _context.SaveChanges();
        }

        var normalizedName = tagName.ToLowerInvariant();
        var tag = _context.BooruTags.FirstOrDefault(t => t.Name == normalizedName && t.Category != null && t.Category.Slug == normalizedSlug);
        if (tag == null)
        {
            tag = new Tag
            {
                Name = normalizedName,
                Category = category,
                PostCount = 0
            };
            _context.BooruTags.Add(tag);
        }
        tag.PostCount++;
        _context.SaveChanges();

        var postTag = new PostTag
        {
            PostId = post.Id,
            TagId = tag.Id,
            AddedAt = DateTime.UtcNow
        };
        _context.Set<PostTag>().Add(postTag);
        post.TagCount = 1;
        _context.SaveChanges();

        return post;
    }

    private Post CreatePostWithRating(int id, PostRating rating, params string[] tagNames)
    {
        var post = MockData.CreateTestPost(_user, id, $"hash{id}");
        post.Rating = rating;
        _context.BooruPosts.Add(post);
        _context.SaveChanges();

        foreach (var tagName in tagNames)
        {
            var normalizedName = tagName.ToLowerInvariant();
            var tag = _context.BooruTags.FirstOrDefault(t => t.Name == normalizedName && t.Category == null);
            if (tag == null)
            {
                tag = new Tag { Name = normalizedName, PostCount = 0 };
                _context.BooruTags.Add(tag);
            }

            tag.PostCount++;
            _context.SaveChanges();

            var postTag = new PostTag
            {
                PostId = post.Id,
                TagId = tag.Id,
                AddedAt = DateTime.UtcNow
            };
            _context.Set<PostTag>().Add(postTag);
        }

        post.TagCount = tagNames.Length;
        _context.SaveChanges();
        return post;
    }

    #endregion

    #region Tag Search - True Positives

    [Fact]
    public void Query_SingleTag_ReturnsMatchingPosts()
    {
        // Arrange
        var post1 = CreatePost(1, "cat");
        var post2 = CreatePost(2, "dog");
        var post3 = CreatePost(3, "cat", "dog");

        var parseResult = new SearchParseResult(
            new TagNode("cat"),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should match posts 1 and 3 (both have "cat")
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 3 });
    }

    [Fact]
    public void Query_MultipleTags_AND_ReturnsPostsWithAllTags()
    {
        // Arrange
        var post1 = CreatePost(1, "cat");
        var post2 = CreatePost(2, "dog");
        var post3 = CreatePost(3, "cat", "dog");
        var post4 = CreatePost(4, "cat", "bird");

        var parseResult = new SearchParseResult(
            new AndNode(new List<SearchNode>
            {
                new TagNode("cat"),
                new TagNode("dog")
            }),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should match only post 3 (has both cat AND dog)
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(3);
    }

    #endregion

    #region Tag Search - False Negatives (Missing posts that should match)

    [Fact]
    public void Query_TagNotFound_ReturnsEmpty()
    {
        // Arrange
        var post1 = CreatePost(1, "cat");
        var post2 = CreatePost(2, "dog");

        var parseResult = new SearchParseResult(
            new TagNode("bird"),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return empty (no false negatives here, just no matches)
        results.Should().BeEmpty();
    }

    [Fact]
    public void Query_TagCaseSensitivity_ShouldBeCaseInsensitive()
    {
        // Arrange - Tags are stored lowercase
        var post1 = CreatePost(1, "cat");

        // Search with different case - should still match
        var parseResult = new SearchParseResult(
            new TagNode("CAT"), // TagNode constructor lowercases
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should match despite case difference
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    #endregion

    #region Tag Search - False Positives (Posts that shouldn't match)

    [Fact]
    public void Query_NegatedTag_ExcludesPostsWithTag()
    {
        // Arrange
        var post1 = CreatePost(1, "cat");
        var post2 = CreatePost(2, "dog");
        var post3 = CreatePost(3, "cat", "dog");

        var parseResult = new SearchParseResult(
            new TagNode("cat", negated: true),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should only return post 2 (doesn't have cat)
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(2);
    }

    [Fact]
    public void Query_TagWithNegation_CorrectExclusion()
    {
        // Arrange
        var post1 = CreatePost(1, "cat");
        var post2 = CreatePost(2, "cat", "dog");
        var post3 = CreatePost(3, "bird");

        var parseResult = new SearchParseResult(
            new AndNode(new List<SearchNode>
            {
                new TagNode("cat"),
                new TagNode("dog", negated: true)
            }),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return only post 1 (has cat, but NOT dog)
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    #endregion

    #region OR Groups

    [Fact]
    public void Query_OrGroup_ReturnsPostsWithAnyTag()
    {
        // Arrange
        var post1 = CreatePost(1, "cat");
        var post2 = CreatePost(2, "dog");
        var post3 = CreatePost(3, "bird");
        var post4 = CreatePost(4, "cat", "dog");

        var parseResult = new SearchParseResult(
            new OrNode(new List<SearchNode>
            {
                new TagNode("cat"),
                new TagNode("dog")
            }),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts 1, 2, 4 (has cat OR dog)
        results.Should().HaveCount(3);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 2, 4 });
    }

    [Fact]
    public void Query_OrGroupWithAnd_ComplexLogic()
    {
        // Arrange - Query: bird AND (cat OR dog)
        var post1 = CreatePost(1, "bird", "cat");
        var post2 = CreatePost(2, "bird", "dog");
        var post3 = CreatePost(3, "bird");
        var post4 = CreatePost(4, "cat", "dog");
        var post5 = CreatePost(5, "bird", "cat", "dog");

        var parseResult = new SearchParseResult(
            new AndNode(new List<SearchNode>
            {
                new TagNode("bird"),
                new OrNode(new List<SearchNode>
                {
                    new TagNode("cat"),
                    new TagNode("dog")
                })
            }),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts 1, 2, 5 (has bird AND (cat OR dog))
        results.Should().HaveCount(3);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 2, 5 });
    }

    #endregion

    #region Wildcards

    [Fact]
    public void Query_Wildcard_MatchesPrefix()
    {
        // Arrange
        var post1 = CreatePost(1, "cat");
        var post2 = CreatePost(2, "catgirl");
        var post3 = CreatePost(3, "category");
        var post4 = CreatePost(4, "dog");

        var parseResult = new SearchParseResult(
            new WildcardTagNode("cat"),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should match posts 1, 2, 3 (tags starting with "cat")
        results.Should().HaveCount(3);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public void Query_NegatedWildcard_ExcludesPrefix()
    {
        // Arrange
        var post1 = CreatePost(1, "cat");
        var post2 = CreatePost(2, "catgirl");
        var post3 = CreatePost(3, "dog");

        var parseResult = new SearchParseResult(
            new WildcardTagNode("cat", negated: true),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should only return post 3 (no tags starting with "cat")
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(3);
    }

    #endregion

    #region Rating Filter

    [Fact]
    public void Query_RatingSafe_ReturnsOnlySafePosts()
    {
        // Arrange
        var post1 = CreatePostWithRating(1, PostRating.Safe, "cat");
        var post2 = CreatePostWithRating(2, PostRating.Questionable, "dog");
        var post3 = CreatePostWithRating(3, PostRating.Explicit, "bird");
        var post4 = CreatePostWithRating(4, PostRating.Safe, "fish");

        var parseResult = new SearchParseResult(
            new RatingFilterNode(PostRating.Safe),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 4 });
    }

    [Fact]
    public void Query_NegatedRating_ExcludesRating()
    {
        // Arrange
        var post1 = CreatePostWithRating(1, PostRating.Safe, "cat");
        var post2 = CreatePostWithRating(2, PostRating.Questionable, "dog");
        var post3 = CreatePostWithRating(3, PostRating.Explicit, "bird");

        var parseResult = new SearchParseResult(
            new RatingFilterNode(PostRating.Safe) { Negated = true },
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts 2 and 3 (NOT safe)
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 2, 3 });
    }

    [Fact]
    public void Query_TagWithRating_CombinesFilters()
    {
        // Arrange
        var post1 = CreatePostWithRating(1, PostRating.Safe, "cat");
        var post2 = CreatePostWithRating(2, PostRating.Questionable, "cat");
        var post3 = CreatePostWithRating(3, PostRating.Safe, "dog");

        var parseResult = new SearchParseResult(
            new AndNode(new List<SearchNode>
            {
                new TagNode("cat"),
                new RatingFilterNode(PostRating.Safe)
            }),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return only post 1 (has cat AND is safe)
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    #endregion

    #region Trashed Status Filter

    [Fact]
    public void Query_StatusTrashed_WithViewTrashPermission_ReturnsTrashedPosts()
    {
        // Arrange
        var visiblePost = CreatePost(1, "cat");
        var trashedPost = CreatePost(2, "cat");
        trashedPost.IsTrashed = true;
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new StatusFilterNode("trashed"),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(2);
    }

    [Fact]
    public void Query_StatusTrashed_WithoutViewTrashPermission_ReturnsEmpty()
    {
        // Arrange
        var visiblePost = CreatePost(1, "cat");
        var trashedPost = CreatePost(2, "cat");
        trashedPost.IsTrashed = true;
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new StatusFilterNode("trashed"),
            null,
            new List<string>(),
            new List<string>()
        );

        var anonymousBuilder = new SearchQueryBuilder(_context, new AnonymousUserContext());

        // Act
        var query = anonymousBuilder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void Query_WithoutTrashedStatus_StillExcludesTrashedPosts()
    {
        // Arrange
        var visiblePost = CreatePost(1, "cat");
        var trashedPost = CreatePost(2, "cat");
        trashedPost.IsTrashed = true;
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new TagNode("cat"),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    [Fact]
    public void Query_OrGroupWithTrashedStatus_CombinesCorrectly()
    {
        // Arrange
        var catPost = CreatePost(1, "cat");
        var dogPost = CreatePost(2, "dog");
        var trashedBirdPost = CreatePost(3, "bird");
        trashedBirdPost.IsTrashed = true;
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new OrNode(new List<SearchNode>
            {
                new StatusFilterNode("trashed"),
                new TagNode("cat")
            }),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 3 });
    }

    #endregion

    #region Numeric Filters

    [Fact]
    public void Query_IdGreaterThan_ReturnsMatchingIds()
    {
        // Arrange
        var post1 = CreatePost(1, "cat");
        var post2 = CreatePost(2, "dog");
        var post3 = CreatePost(3, "bird");

        var parseResult = new SearchParseResult(
            new NumericRangeFilterNode("id", min: 2, max: null),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts 2 and 3 (id >= 2)
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 2, 3 });
    }

    [Fact]
    public void Query_IdRange_ReturnsPostsInRange()
    {
        // Arrange
        var post1 = CreatePost(1, "cat");
        var post2 = CreatePost(2, "dog");
        var post3 = CreatePost(3, "bird");
        var post4 = CreatePost(4, "fish");

        var parseResult = new SearchParseResult(
            new NumericRangeFilterNode("id", min: 2, max: 3),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts 2 and 3
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 2, 3 });
    }

    #endregion

    #region Category-Prefixed Tags

    /// <summary>
    /// Category-prefixed tags like "character:naruto" should search by both
    /// category slug and tag name.
    /// </summary>
    [Fact]
    public void Query_CategoryPrefixedTag_SearchesByCategoryAndName()
    {
        // Arrange - Create posts with categorized tags
        var post1 = CreatePostWithCategory(1, "character", "naruto");
        var post2 = CreatePostWithCategory(2, "character", "sasuke");
        var post3 = CreatePost(3, "naruto"); // Same name, no category

        var parseResult = new SearchParseResult(
            new CategoryTagNode("character", "naruto"),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should only return post 1 (character:naruto)
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    /// <summary>
    /// Different categories with same tag name should not match.
    /// </summary>
    [Fact]
    public void Query_CategoryTag_DifferentiatesBetweenCategories()
    {
        // Arrange
        var post1 = CreatePostWithCategory(1, "character", "naruto");
        var post2 = CreatePostWithCategory(2, "artist", "naruto"); // Same name, different category
        var post3 = CreatePost(3, "naruto"); // No category

        var parseResult = new SearchParseResult(
            new CategoryTagNode("character", "naruto"),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should only return post 1 (character:naruto, not artist:naruto)
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    /// <summary>
    /// Negated category tag should exclude matching posts.
    /// </summary>
    [Fact]
    public void Query_NegatedCategoryTag_ExcludesMatchingPosts()
    {
        // Arrange
        var post1 = CreatePostWithCategory(1, "character", "naruto");
        var post2 = CreatePostWithCategory(2, "artist", "naruto");
        var post3 = CreatePost(3, "dog");

        var parseResult = new SearchParseResult(
            new CategoryTagNode("character", "naruto", negated: true),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts 2 and 3 (not character:naruto)
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 2, 3 });
    }

    /// <summary>
    /// Searching by tag name only should match all categories.
    /// </summary>
    [Fact]
    public void Query_TagNameOnly_MatchesAllCategories()
    {
        // Arrange
        var post1 = CreatePostWithCategory(1, "character", "naruto");
        var post2 = CreatePostWithCategory(2, "artist", "naruto");
        var post3 = CreatePost(3, "naruto"); // No category

        // Search for just "naruto" without category
        var parseResult = new SearchParseResult(
            new TagNode("naruto"),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return all 3 posts (all have tag name "naruto")
        results.Should().HaveCount(3);
    }

    /// <summary>
    /// Multiple category tags with AND logic.
    /// </summary>
    [Fact]
    public void Query_MultipleCategoryTags_AND_ReturnsPostsWithAll()
    {
        // Arrange - Use seeded categories
        var category1 = _context.BooruTagCategories.First(c => c.Slug == "character");
        var category2 = _context.BooruTagCategories.First(c => c.Slug == "series");

        var tag1 = new Tag { Name = "naruto", Category = category1, PostCount = 1 };
        var tag2 = new Tag { Name = "shonen", Category = category2, PostCount = 1 };
        var tag3 = new Tag { Name = "sasuke", Category = category1, PostCount = 1 };
        _context.BooruTags.AddRange(tag1, tag2, tag3);
        _context.SaveChanges();

        // Post 1 has both character:naruto AND series:shonen
        var post1 = MockData.CreateTestPost(_user, 1, "hash1");
        _context.BooruPosts.Add(post1);
        _context.SaveChanges();
        _context.Set<PostTag>().AddRange(
            new PostTag { PostId = post1.Id, TagId = tag1.Id, AddedAt = DateTime.UtcNow },
            new PostTag { PostId = post1.Id, TagId = tag2.Id, AddedAt = DateTime.UtcNow }
        );

        // Post 2 has only character:naruto
        var post2 = MockData.CreateTestPost(_user, 2, "hash2");
        _context.BooruPosts.Add(post2);
        _context.SaveChanges();
        _context.Set<PostTag>().Add(
            new PostTag { PostId = post2.Id, TagId = tag1.Id, AddedAt = DateTime.UtcNow }
        );

        // Post 3 has character:sasuke and series:shonen
        var post3 = MockData.CreateTestPost(_user, 3, "hash3");
        _context.BooruPosts.Add(post3);
        _context.SaveChanges();
        _context.Set<PostTag>().AddRange(
            new PostTag { PostId = post3.Id, TagId = tag3.Id, AddedAt = DateTime.UtcNow },
            new PostTag { PostId = post3.Id, TagId = tag2.Id, AddedAt = DateTime.UtcNow }
        );

        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new AndNode(new List<SearchNode>
            {
                new CategoryTagNode("character", "naruto"),
                new CategoryTagNode("series", "shonen")
            }),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should only return post 1
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    #endregion

    #region Ordering

    [Fact]
    public void Query_OrderByIdDescending_OrdersCorrectly()
    {
        // Arrange
        CreatePost(1, "cat");
        CreatePost(2, "dog");
        CreatePost(3, "bird");

        var parseResult = new SearchParseResult(
            null,
            new OrderByNode("id", descending: true),
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should be ordered 3, 2, 1
        results.Select(r => r.Id).Should().ContainInOrder(3, 2, 1);
    }

    [Fact]
    public void Query_OrderByIdAscending_OrdersCorrectly()
    {
        // Arrange
        CreatePost(3, "bird");
        CreatePost(1, "cat");
        CreatePost(2, "dog");

        var parseResult = new SearchParseResult(
            null,
            new OrderByNode("id", descending: false),
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should be ordered 1, 2, 3
        results.Select(r => r.Id).Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public void Query_DefaultOrder_IsByDateDescending()
    {
        // Arrange - Create posts with different dates
        var post1 = CreatePost(1, "cat");
        post1.UploadedAt = DateTime.UtcNow.AddDays(-2);
        var post2 = CreatePost(2, "dog");
        post2.UploadedAt = DateTime.UtcNow.AddDays(-1);
        var post3 = CreatePost(3, "bird");
        post3.UploadedAt = DateTime.UtcNow;
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            null, // No filter
            null, // No explicit ordering
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should be ordered by date descending (newest first): 3, 2, 1
        results.Select(r => r.Id).Should().ContainInOrder(3, 2, 1);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Query_PostWithNoTags_HandledCorrectly()
    {
        // Arrange - Create post without tags
        var post = MockData.CreateTestPost(_user, 1, "hash1");
        _context.BooruPosts.Add(post);
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new TagNode("cat"),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should not return the post (no tags)
        results.Should().BeEmpty();
    }

    [Fact]
    public void Query_NullRootNode_ReturnsAllPosts()
    {
        // Arrange
        CreatePost(1, "cat");
        CreatePost(2, "dog");

        var parseResult = new SearchParseResult(
            null, // No filter
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return all posts
        results.Should().HaveCount(2);
    }

    [Fact]
    public void Query_PostWithMultipleTags_MatchesAnyOfThem()
    {
        // Arrange
        var post1 = CreatePost(1, "cat", "dog", "bird");
        var post2 = CreatePost(2, "fish");

        var parseResult = new SearchParseResult(
            new TagNode("dog"),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should match post with "dog" tag
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    #endregion

    #region Uploader Filter

    [Fact]
    public void Query_UploaderFilter_ReturnsPostsByUser()
    {
        // Arrange
        var user2 = MockData.CreateTestUser("anotheruser", "another@example.com");
        _context.Users.Add(user2);
        _context.SaveChanges();

        var post1 = CreatePost(1, "cat");
        var post2 = MockData.CreateTestPost(user2, 2, "hash2");
        _context.BooruPosts.Add(post2);
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new UploaderFilterNode("testuser"),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should only return post by testuser
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    [Fact]
    public void Query_NegatedUploaderFilter_ExcludesPostsByUser()
    {
        // Arrange
        var user2 = MockData.CreateTestUser("anotheruser", "another@example.com");
        _context.Users.Add(user2);
        _context.SaveChanges();

        CreatePost(1, "cat");
        var post2 = MockData.CreateTestPost(user2, 2, "hash2");
        _context.BooruPosts.Add(post2);
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new UploaderFilterNode("testuser") { Negated = true },
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should only return post NOT by testuser
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(2);
    }

    [Fact]
    public void Query_UploaderFilter_CaseInsensitive()
    {
        // Arrange
        CreatePost(1, "cat");

        var parseResult = new SearchParseResult(
            new UploaderFilterNode("TESTUSER"), // Uppercase
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should match despite case difference
        results.Should().HaveCount(1);
    }

    #endregion

    #region FileType Filter

    [Fact]
    public void Query_FileTypeFilter_ReturnsMatchingMimeType()
    {
        // Arrange
        var post1 = MockData.CreateTestPost(_user, 1, "hash1", "image/jpeg");
        var post2 = MockData.CreateTestPost(_user, 2, "hash2", "image/png");
        var post3 = MockData.CreateTestPost(_user, 3, "hash3", "video/webm");
        _context.BooruPosts.AddRange(post1, post2, post3);
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new FileTypeFilterNode("image/png"),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(2);
    }

    [Fact]
    public void Query_NegatedFileTypeFilter_ExcludesMimeType()
    {
        // Arrange
        var post1 = MockData.CreateTestPost(_user, 1, "hash1", "image/jpeg");
        var post2 = MockData.CreateTestPost(_user, 2, "hash2", "image/png");
        var post3 = MockData.CreateTestPost(_user, 3, "hash3", "video/webm");
        _context.BooruPosts.AddRange(post1, post2, post3);
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new FileTypeFilterNode("image/jpeg") { Negated = true },
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts 2 and 3
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 2, 3 });
    }

    #endregion

    #region TagCount Filter

    [Fact]
    public void Query_TagCountMin_ReturnsPostsWithEnoughTags()
    {
        // Arrange
        CreatePost(1, "cat");
        CreatePost(2, "cat", "dog");
        CreatePost(3, "cat", "dog", "bird");

        var parseResult = new SearchParseResult(
            new NumericRangeFilterNode("tagcount", min: 2, max: null),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts with >= 2 tags
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 2, 3 });
    }

    [Fact]
    public void Query_TagCountMax_ReturnsPostsWithFewTags()
    {
        // Arrange
        CreatePost(1, "cat");
        CreatePost(2, "cat", "dog");
        CreatePost(3, "cat", "dog", "bird");

        var parseResult = new SearchParseResult(
            new NumericRangeFilterNode("tagcount", min: null, max: 1),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts with <= 1 tags
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    [Fact]
    public void Query_TagCountRange_ReturnsPostsInRange()
    {
        // Arrange
        CreatePost(1, "cat");
        CreatePost(2, "cat", "dog");
        CreatePost(3, "cat", "dog", "bird");
        CreatePost(4, "a", "b", "c", "d");

        var parseResult = new SearchParseResult(
            new NumericRangeFilterNode("tagcount", min: 2, max: 3),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts with 2-3 tags
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 2, 3 });
    }

    #endregion

    #region Date Filter

    [Fact]
    public void Query_DateRangeMin_ReturnsPostsAfterDate()
    {
        // Arrange
        var post1 = MockData.CreateTestPost(_user, 1, "hash1");
        post1.UploadedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var post2 = MockData.CreateTestPost(_user, 2, "hash2");
        post2.UploadedAt = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var post3 = MockData.CreateTestPost(_user, 3, "hash3");
        post3.UploadedAt = new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        _context.BooruPosts.AddRange(post1, post2, post3);
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new DateRangeFilterNode(min: new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), max: null),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts after June 1st
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 2, 3 });
    }

    [Fact]
    public void Query_DateRangeMax_ReturnsPostsBeforeDate()
    {
        // Arrange
        var post1 = MockData.CreateTestPost(_user, 1, "hash1");
        post1.UploadedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var post2 = MockData.CreateTestPost(_user, 2, "hash2");
        post2.UploadedAt = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var post3 = MockData.CreateTestPost(_user, 3, "hash3");
        post3.UploadedAt = new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        _context.BooruPosts.AddRange(post1, post2, post3);
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new DateRangeFilterNode(min: null, max: new DateTime(2024, 6, 30, 23, 59, 59, DateTimeKind.Utc)),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts before end of June
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    #endregion

    #region Width/Height Filters

    [Fact]
    public void Query_WidthFilter_ReturnsMatchingPosts()
    {
        // Arrange
        var post1 = MockData.CreateTestPost(_user, 1, "hash1");
        post1.Width = 1920;
        post1.Height = 1080;
        var post2 = MockData.CreateTestPost(_user, 2, "hash2");
        post2.Width = 1280;
        post2.Height = 720;
        var post3 = MockData.CreateTestPost(_user, 3, "hash3");
        post3.Width = 3840;
        post3.Height = 2160;
        _context.BooruPosts.AddRange(post1, post2, post3);
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new NumericRangeFilterNode("width", min: 1920, max: null),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts with width >= 1920
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 3 });
    }

    [Fact]
    public void Query_HeightFilter_ReturnsMatchingPosts()
    {
        // Arrange
        var post1 = MockData.CreateTestPost(_user, 1, "hash1");
        post1.Width = 1920;
        post1.Height = 1080;
        var post2 = MockData.CreateTestPost(_user, 2, "hash2");
        post2.Width = 1280;
        post2.Height = 720;
        _context.BooruPosts.AddRange(post1, post2);
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new NumericRangeFilterNode("height", min: null, max: 1000),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts with height <= 1000
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(2);
    }

    #endregion

    #region FileSize Filter

    [Fact]
    public void Query_FileSizeFilter_ReturnsMatchingPosts()
    {
        // Arrange
        var post1 = MockData.CreateTestPost(_user, 1, "hash1");
        post1.FileSize = 100_000; // 100KB
        var post2 = MockData.CreateTestPost(_user, 2, "hash2");
        post2.FileSize = 1_000_000; // 1MB
        var post3 = MockData.CreateTestPost(_user, 3, "hash3");
        post3.FileSize = 10_000_000; // 10MB
        _context.BooruPosts.AddRange(post1, post2, post3);
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new NumericRangeFilterNode("filesize", min: 500_000, max: null),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts with filesize >= 500KB
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 2, 3 });
    }

    #endregion

    #region Complex Combined Queries

    [Fact]
    public void Query_TagAndRatingAndUploader_CombinesAllFilters()
    {
        // Arrange
        var user2 = MockData.CreateTestUser("otheruser", "other@example.com");
        _context.Users.Add(user2);
        _context.SaveChanges();

        var post1 = CreatePostWithRating(1, PostRating.Safe, "cat");
        var post2 = CreatePostWithRating(2, PostRating.Questionable, "cat");
        var post3 = CreatePostWithRating(3, PostRating.Safe, "dog");
        var post4 = MockData.CreateTestPost(user2, 4, "hash4");
        post4.Rating = PostRating.Safe;
        _context.BooruPosts.Add(post4);
        _context.SaveChanges();

        // Add "cat" tag to post4
        var catTag = new Tag { Name = "cat", PostCount = 1 };
        _context.BooruTags.Add(catTag);
        _context.SaveChanges();
        _context.Set<PostTag>().Add(new PostTag { PostId = 4, TagId = catTag.Id, AddedAt = DateTime.UtcNow });
        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new AndNode(new List<SearchNode>
            {
                new TagNode("cat"),
                new RatingFilterNode(PostRating.Safe),
                new UploaderFilterNode("testuser")
            }),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should only return post 1 (cat + safe + testuser)
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    [Fact]
    public void Query_OrGroupWithCategoryTags_WorksCorrectly()
    {
        // Arrange
        var post1 = CreatePostWithCategory(1, "character", "naruto");
        var post2 = CreatePostWithCategory(2, "character", "sasuke");
        var post3 = CreatePostWithCategory(3, "artist", "kishimoto");

        var parseResult = new SearchParseResult(
            new OrNode(new List<SearchNode>
            {
                new CategoryTagNode("character", "naruto"),
                new CategoryTagNode("character", "sasuke")
            }),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts 1 and 2
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void Query_MixedTagsAndCategoryTags_AND_WorksCorrectly()
    {
        // Arrange - Use seeded category
        var category = _context.BooruTagCategories.First(c => c.Slug == "character");

        var categoryTag = new Tag { Name = "naruto", Category = category, PostCount = 1 };
        var regularTag = new Tag { Name = "action", PostCount = 1 };
        _context.BooruTags.AddRange(categoryTag, regularTag);
        _context.SaveChanges();

        // Post 1: character:naruto + action
        var post1 = MockData.CreateTestPost(_user, 1, "hash1");
        _context.BooruPosts.Add(post1);
        _context.SaveChanges();
        _context.Set<PostTag>().AddRange(
            new PostTag { PostId = 1, TagId = categoryTag.Id, AddedAt = DateTime.UtcNow },
            new PostTag { PostId = 1, TagId = regularTag.Id, AddedAt = DateTime.UtcNow }
        );

        // Post 2: only character:naruto
        var post2 = MockData.CreateTestPost(_user, 2, "hash2");
        _context.BooruPosts.Add(post2);
        _context.SaveChanges();
        _context.Set<PostTag>().Add(
            new PostTag { PostId = 2, TagId = categoryTag.Id, AddedAt = DateTime.UtcNow }
        );

        // Post 3: only action
        var post3 = MockData.CreateTestPost(_user, 3, "hash3");
        _context.BooruPosts.Add(post3);
        _context.SaveChanges();
        _context.Set<PostTag>().Add(
            new PostTag { PostId = 3, TagId = regularTag.Id, AddedAt = DateTime.UtcNow }
        );

        _context.SaveChanges();

        var parseResult = new SearchParseResult(
            new AndNode(new List<SearchNode>
            {
                new CategoryTagNode("character", "naruto"),
                new TagNode("action")
            }),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should only return post 1
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    [Fact]
    public void Query_NegatedTagWithPositiveTag_ExcludesCorrectly()
    {
        // Arrange
        CreatePost(1, "cat", "cute");
        CreatePost(2, "cat", "scary");
        CreatePost(3, "dog", "cute");

        var parseResult = new SearchParseResult(
            new AndNode(new List<SearchNode>
            {
                new TagNode("cat"),
                new TagNode("scary", negated: true)
            }),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return post 1 (cat but not scary)
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    [Fact]
    public void Query_WildcardInOrGroup_WorksCorrectly()
    {
        // Arrange
        CreatePost(1, "catgirl");
        CreatePost(2, "doggirl");
        CreatePost(3, "bird");

        var parseResult = new SearchParseResult(
            new OrNode(new List<SearchNode>
            {
                new WildcardTagNode("cat"),
                new WildcardTagNode("dog")
            }),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return posts 1 and 2
        results.Should().HaveCount(2);
        results.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    #endregion

    #region False Positive/Negative Edge Cases

    [Fact]
    public void Query_SimilarTagNames_NoFalsePositives()
    {
        // Arrange - Tags that could be confused
        CreatePost(1, "cat");
        CreatePost(2, "catgirl");
        CreatePost(3, "category");

        var parseResult = new SearchParseResult(
            new TagNode("cat"), // Exact match, not wildcard
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should ONLY return post 1 (exact match)
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    [Fact]
    public void Query_SubstringTagNames_NoFalsePositives()
    {
        // Arrange
        CreatePost(1, "art");
        CreatePost(2, "artist");
        CreatePost(3, "martial_arts");

        var parseResult = new SearchParseResult(
            new TagNode("art"),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should ONLY return post 1
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
    }

    [Fact]
    public void Query_EmptyStringTag_HandlesGracefully()
    {
        // Arrange
        CreatePost(1, "cat");

        var parseResult = new SearchParseResult(
            new TagNode(""),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return nothing (no tag with empty name)
        results.Should().BeEmpty();
    }

    [Fact]
    public void Query_CategoryTagWithNonexistentCategory_ReturnsEmpty()
    {
        // Arrange
        CreatePost(1, "naruto"); // Regular tag, no category

        var parseResult = new SearchParseResult(
            new CategoryTagNode("nonexistent", "naruto"),
            null,
            new List<string>(),
            new List<string>()
        );

        // Act
        var query = _builder.BuildQuery(parseResult);
        var results = query.ToList();

        // Assert - Should return nothing
        results.Should().BeEmpty();
    }

    #endregion
}

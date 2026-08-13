using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.Data;
using Nuuru.Server.Models;
using Nuuru.Server.Models.Forum;
using Nuuru.Server.Services;
using Nuuru.Server.Services.BBCode;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class ForumPostServiceTests
{
    private static readonly Guid User1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid User2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CategoryId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly ApplicationDbContext _context;
    private readonly ForumPostService _sut;

    public ForumPostServiceTests()
    {
        _context = TestDbContextFactory.CreateWithSeedData();

        _sut = new ForumPostService(
            _context,
            Mock.Of<IBBCodeService>(),
            Mock.Of<INotificationService>(),
            Mock.Of<IWatchService>(),
            Mock.Of<IHtmlEnrichmentService>(),
            Mock.Of<IForumPostHtmlEnrichmentScheduler>(),
            Mock.Of<IForumAttachmentService>(),
            Mock.Of<IUserSettingsService>(),
            Mock.Of<IBointsService>(),
            Mock.Of<ILogger<ForumPostService>>());
    }

    private ForumThread CreateThread()
    {
        var category = new ForumCategory
        {
            Id = CategoryId,
            Slug = "test",
            Name = "Test"
        };
        // Only add if not already present
        if (!_context.ForumCategories.Any(c => c.Id == CategoryId))
            _context.ForumCategories.Add(category);

        var thread = new ForumThread
        {
            Title = "Test Thread",
            CategoryId = CategoryId,
            AuthorId = User1Id,
            CreatedAt = DateTime.UtcNow
        };
        _context.ForumThreads.Add(thread);
        _context.SaveChanges();
        return thread;
    }

    private ForumPost CreatePost(int threadId, Guid authorId, int minutesOffset = 0)
    {
        var post = new ForumPost
        {
            ThreadId = threadId,
            AuthorId = authorId,
            ContentRaw = "test",
            ContentHtml = "<p>test</p>",
            CreatedAt = DateTime.UtcNow.AddMinutes(minutesOffset)
        };
        _context.ForumPosts.Add(post);
        _context.SaveChanges();
        return post;
    }

    private void AddReactions(int postId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            _context.Set<Reaction>().Add(new Reaction
            {
                EntityType = ReactionEntityType.ForumPost,
                EntityId = postId,
                UserId = User1Id,
                EmoteName = "like",
                CreatedAt = DateTime.UtcNow
            });
        }
        _context.SaveChanges();
    }

    // ==================== GetHighlightedPostIdsAsync ====================

    [Fact]
    public async Task GetHighlightedPostIds_NoReactions_ReturnsEmpty()
    {
        var thread = CreateThread();
        CreatePost(thread.Id, User1Id);

        var result = await _sut.GetHighlightedPostIdsAsync(thread.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHighlightedPostIds_BelowMinReactions_ReturnsEmpty()
    {
        var thread = CreateThread();
        var post = CreatePost(thread.Id, User1Id);
        AddReactions(post.Id, 2);

        // minReactions=3, so 2 reactions won't qualify
        var result = await _sut.GetHighlightedPostIdsAsync(thread.Id, minReactions: 3);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHighlightedPostIds_SinglePostAboveMin_ReturnsIt()
    {
        var thread = CreateThread();
        var post = CreatePost(thread.Id, User1Id);
        AddReactions(post.Id, 5);

        var result = await _sut.GetHighlightedPostIdsAsync(thread.Id, minReactions: 1);

        result.Should().ContainSingle().Which.Should().Be(post.Id);
    }

    [Fact]
    public async Task GetHighlightedPostIds_TakesTopTwentyPercent()
    {
        var thread = CreateThread();

        // Create 20 posts with increasing reaction counts
        var posts = new List<ForumPost>();
        for (var i = 0; i < 20; i++)
        {
            var post = CreatePost(thread.Id, User1Id, minutesOffset: i);
            AddReactions(post.Id, i + 1); // 1, 2, 3, ... 20 reactions
            posts.Add(post);
        }

        // Top 10% of 20 = ceil(2) = 2 posts
        var result = await _sut.GetHighlightedPostIdsAsync(thread.Id, minReactions: 1);

        result.Should().HaveCount(2);
        // Posts with 20, 19 reactions
        result.Should().Contain(posts[19].Id);
        result.Should().Contain(posts[18].Id);
    }

    [Fact]
    public async Task GetHighlightedPostIds_TopTwentyPercentCeiling()
    {
        var thread = CreateThread();

        // 3 posts: ceil(3 * 0.2) = ceil(0.6) = 1 highlighted
        var post1 = CreatePost(thread.Id, User1Id, minutesOffset: 0);
        var post2 = CreatePost(thread.Id, User1Id, minutesOffset: 1);
        var post3 = CreatePost(thread.Id, User1Id, minutesOffset: 2);
        AddReactions(post1.Id, 10);
        AddReactions(post2.Id, 5);
        AddReactions(post3.Id, 1);

        var result = await _sut.GetHighlightedPostIdsAsync(thread.Id, minReactions: 1);

        result.Should().ContainSingle().Which.Should().Be(post1.Id);
    }

    [Fact]
    public async Task GetHighlightedPostIds_ResultsOrderedByPostId()
    {
        var thread = CreateThread();

        // Create 20 posts; top 10% of 20 = ceil(2) = 2
        var posts = new List<ForumPost>();
        for (var i = 0; i < 20; i++)
        {
            var post = CreatePost(thread.Id, User1Id, minutesOffset: i);
            posts.Add(post);
        }
        // Give highest reactions to an early and a late post
        AddReactions(posts[1].Id, 20); // early post, high reactions
        AddReactions(posts[8].Id, 15); // late post, also high
        // Give minimal reactions to others
        for (var i = 0; i < 20; i++)
        {
            if (i != 1 && i != 8)
                AddReactions(posts[i].Id, 1);
        }

        var result = await _sut.GetHighlightedPostIdsAsync(thread.Id, minReactions: 2);

        result.Should().HaveCount(2);
        // Should be sorted by post ID (chronological), not by reaction count
        result.Should().BeInAscendingOrder();
        result[0].Should().Be(posts[1].Id);
        result[1].Should().Be(posts[8].Id);
    }

    [Fact]
    public async Task GetHighlightedPostIds_IgnoresReactionsFromOtherThreads()
    {
        var thread1 = CreateThread();
        var thread2 = CreateThread();

        var post1 = CreatePost(thread1.Id, User1Id);
        var post2 = CreatePost(thread2.Id, User1Id);
        AddReactions(post1.Id, 1);
        AddReactions(post2.Id, 10);

        var result = await _sut.GetHighlightedPostIdsAsync(thread1.Id, minReactions: 1);

        result.Should().ContainSingle().Which.Should().Be(post1.Id);
    }

    [Fact]
    public async Task GetHighlightedPostIds_IgnoresNonForumPostReactions()
    {
        var thread = CreateThread();
        var post = CreatePost(thread.Id, User1Id);

        // Add BooruComment reactions with the same EntityId
        _context.Set<Reaction>().Add(new Reaction
        {
            EntityType = ReactionEntityType.BooruComment,
            EntityId = post.Id,
            UserId = User1Id,
            EmoteName = "like"
        });
        _context.SaveChanges();

        var result = await _sut.GetHighlightedPostIdsAsync(thread.Id, minReactions: 1);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHighlightedPostIds_DefaultMinReactions_IsOne()
    {
        var thread = CreateThread();
        var post = CreatePost(thread.Id, User1Id);
        AddReactions(post.Id, 5);

        // Default minReactions is 5
        var result = await _sut.GetHighlightedPostIdsAsync(thread.Id);

        result.Should().ContainSingle().Which.Should().Be(post.Id);
    }

    [Fact]
    public async Task GetHighlightedPostIds_EmptyThread_ReturnsEmpty()
    {
        var thread = CreateThread();

        var result = await _sut.GetHighlightedPostIdsAsync(thread.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHighlightedPostIds_FewReactedPostsAmongMany_UsesTotalPostCount()
    {
        var thread = CreateThread();

        // Create 50 posts, only 2 have reactions
        var posts = new List<ForumPost>();
        for (var i = 0; i < 50; i++)
        {
            posts.Add(CreatePost(thread.Id, User1Id, minutesOffset: i));
        }
        AddReactions(posts[10].Id, 3);
        AddReactions(posts[30].Id, 3);

        // Top 20% of 50 = 10 slots, so both reacted posts should qualify
        var result = await _sut.GetHighlightedPostIdsAsync(thread.Id, minReactions: 1);

        result.Should().HaveCount(2);
        result.Should().Contain(posts[10].Id);
        result.Should().Contain(posts[30].Id);
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nuuru.Server.Data;
using Nuuru.Server.Models;
using Nuuru.Server.Models.Forum;
using Nuuru.Server.Services.Search.Forum;
using Nuuru.Server.Services.Search.Nodes;
using Nuuru.Server.Tests.Helpers;
using ForumCategoryFilterNode = Nuuru.Server.Services.Search.Forum.CategoryFilterNode;

namespace Nuuru.Server.Tests.Unit.Services.Search;

public class ForumSearchQueryBuilderTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ForumSearchQueryBuilder _builder;
    private readonly ApplicationUser _user1;
    private readonly ApplicationUser _user2;
    private readonly ForumCategory _generalCategory;
    private readonly ForumCategory _newsCategory;

    public ForumSearchQueryBuilderTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _builder = new ForumSearchQueryBuilder(_context);

        _user1 = MockData.CreateTestUser("alice", "alice@example.com");
        _user2 = MockData.CreateTestUser("bob", "bob@example.com");
        _context.Users.AddRange(_user1, _user2);

        _generalCategory = new ForumCategory
        {
            Id = Guid.NewGuid(),
            Slug = "gen",
            Name = "General",
            Description = "General discussion",
            Color = "#888888"
        };
        _newsCategory = new ForumCategory
        {
            Id = Guid.NewGuid(),
            Slug = "news",
            Name = "News",
            Description = "News and updates",
            Color = "#ff0000"
        };
        _context.ForumCategories.AddRange(_generalCategory, _newsCategory);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    #region Helper Methods

    private ForumThread CreateThread(
        string title,
        ApplicationUser? author = null,
        ForumCategory? category = null,
        bool isPinned = false,
        bool isLocked = false,
        int replyCount = 0,
        int viewCount = 0,
        DateTime? createdAt = null,
        DateTime? lastPostAt = null,
        params string[] postContents)
    {
        author ??= _user1;
        category ??= _generalCategory;

        var thread = new ForumThread
        {
            Title = title,
            AuthorId = author.Id,
            Author = author,
            CategoryId = category.Id,
            Category = category,
            IsPinned = isPinned,
            IsLocked = isLocked,
            ReplyCount = replyCount,
            ViewCount = viewCount,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            LastPostAt = lastPostAt ?? DateTime.UtcNow,
        };

        _context.ForumThreads.Add(thread);
        _context.SaveChanges();

        // Add posts
        foreach (var content in postContents)
        {
            var post = new ForumPost
            {
                ContentRaw = content,
                ContentHtml = $"<p>{content}</p>",
                ThreadId = thread.Id,
                AuthorId = author.Id,
                Author = author,
                CreatedAt = DateTime.UtcNow
            };
            _context.ForumPosts.Add(post);
        }

        _context.SaveChanges();
        return thread;
    }

    private ForumPost AddPost(ForumThread thread, string content, ApplicationUser? author = null)
    {
        author ??= _user1;
        var post = new ForumPost
        {
            ContentRaw = content,
            ContentHtml = $"<p>{content}</p>",
            ThreadId = thread.Id,
            AuthorId = author.Id,
            Author = author,
            CreatedAt = DateTime.UtcNow
        };
        _context.ForumPosts.Add(post);
        _context.SaveChanges();
        return post;
    }

    private ForumSearchParseResult MakeResult(
        SearchNode? root = null,
        OrderByNode? orderBy = null,
        ForumSearchMode mode = ForumSearchMode.All)
    {
        return new ForumSearchParseResult(root, orderBy, new List<string>(), new List<string>(), mode);
    }

    private List<ForumThread> Execute(ForumSearchParseResult result)
    {
        var query = _builder.BuildQuery(result);
        return query.ToList();
    }

    private int ExecuteCount(ForumSearchParseResult result)
    {
        var query = _builder.BuildCountQuery(result);
        return query.Count();
    }

    #endregion

    #region Keyword Search — All Mode (default)

    [Fact]
    public void Keyword_AllMode_MatchesTitle()
    {
        CreateThread("How to train your cat", postContents: ["Some body text"]);
        CreateThread("Dog grooming tips", postContents: ["Other body text"]);

        var result = MakeResult(new KeywordNode("cat"));
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("How to train your cat");
    }

    [Fact]
    public void Keyword_AllMode_MatchesPostContent()
    {
        var t1 = CreateThread("Unrelated title");
        AddPost(t1, "My cat is adorable");
        CreateThread("Another thread", postContents: ["No match here"]);

        var result = MakeResult(new KeywordNode("cat"));
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Unrelated title");
    }

    [Fact]
    public void Keyword_AllMode_MatchesTitleOrPostContent()
    {
        CreateThread("Cat lovers unite", postContents: ["General chat"]);
        var t2 = CreateThread("Random thread");
        AddPost(t2, "I love my cat");
        CreateThread("No match", postContents: ["Nothing relevant"]);

        var result = MakeResult(new KeywordNode("cat"));
        var threads = Execute(result);

        threads.Should().HaveCount(2);
    }

    [Fact]
    public void NegatedKeyword_AllMode_ExcludesTitleAndPostMatches()
    {
        CreateThread("Cat lovers unite", postContents: ["General chat"]);
        var t2 = CreateThread("Random thread");
        AddPost(t2, "I love my cat");
        CreateThread("No match", postContents: ["Nothing relevant"]);

        var result = MakeResult(new KeywordNode("cat", negated: true));
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("No match");
    }

    #endregion

    #region Keyword Search — Thread Mode

    [Fact]
    public void Keyword_ThreadMode_OnlyMatchesTitle()
    {
        CreateThread("Cat discussion", postContents: ["Body text"]);
        var t2 = CreateThread("Unrelated title");
        AddPost(t2, "This post mentions cat");

        var result = MakeResult(new KeywordNode("cat"), mode: ForumSearchMode.Thread);
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Cat discussion");
    }

    [Fact]
    public void NegatedKeyword_ThreadMode_ExcludesTitleMatches()
    {
        CreateThread("Cat discussion", postContents: ["Body"]);
        CreateThread("Dog discussion", postContents: ["Body"]);

        var result = MakeResult(new KeywordNode("cat", negated: true), mode: ForumSearchMode.Thread);
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Dog discussion");
    }

    #endregion

    #region Keyword Search — Post Mode

    [Fact]
    public void Keyword_PostMode_OnlyMatchesPostContent()
    {
        CreateThread("Cat in title", postContents: ["No mention here"]);
        var t2 = CreateThread("Unrelated title");
        AddPost(t2, "This post mentions cat");

        var result = MakeResult(new KeywordNode("cat"), mode: ForumSearchMode.Post);
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Unrelated title");
    }

    [Fact]
    public void NegatedKeyword_PostMode_ExcludesPostContentMatches()
    {
        var t1 = CreateThread("Thread A");
        AddPost(t1, "Cat related content");
        var t2 = CreateThread("Thread B");
        AddPost(t2, "Dog related content");

        var result = MakeResult(new KeywordNode("cat", negated: true), mode: ForumSearchMode.Post);
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Thread B");
    }

    #endregion

    #region Wildcard Keywords

    [Fact]
    public void WildcardKeyword_AllMode_MatchesTitlePrefix()
    {
        CreateThread("Programming tips", postContents: ["Body"]);
        CreateThread("Profile settings", postContents: ["Body"]);
        CreateThread("General chat", postContents: ["Body"]);

        var result = MakeResult(new WildcardKeywordNode("pro"));
        var threads = Execute(result);

        threads.Should().HaveCount(2);
    }

    [Fact]
    public void WildcardKeyword_ThreadMode_OnlyMatchesTitlePrefix()
    {
        CreateThread("Programming tips", postContents: ["Body"]);
        var t2 = CreateThread("General chat");
        AddPost(t2, "This is about programming");

        var result = MakeResult(new WildcardKeywordNode("pro"), mode: ForumSearchMode.Thread);
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Programming tips");
    }

    [Fact]
    public void NegatedWildcard_ExcludesMatches()
    {
        CreateThread("Programming tips", postContents: ["Body"]);
        CreateThread("General chat", postContents: ["Body"]);

        var result = MakeResult(new WildcardKeywordNode("pro", negated: true), mode: ForumSearchMode.Thread);
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("General chat");
    }

    #endregion

    #region AND Filter

    [Fact]
    public void AndNode_AllChildrenMustMatch()
    {
        CreateThread("Cat and dog lovers", postContents: ["Body"]);
        CreateThread("Cat lovers only", postContents: ["Body"]);
        CreateThread("Dog lovers only", postContents: ["Body"]);

        var result = MakeResult(
            new AndNode(new List<SearchNode>
            {
                new KeywordNode("cat"),
                new KeywordNode("dog")
            }),
            mode: ForumSearchMode.Thread
        );
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Cat and dog lovers");
    }

    #endregion

    #region OR Filter

    [Fact]
    public void OrNode_AnyChildCanMatch()
    {
        CreateThread("Cat lovers", postContents: ["Body"]);
        CreateThread("Dog lovers", postContents: ["Body"]);
        CreateThread("Bird lovers", postContents: ["Body"]);

        var result = MakeResult(
            new OrNode(new List<SearchNode>
            {
                new KeywordNode("cat"),
                new KeywordNode("dog")
            }),
            mode: ForumSearchMode.Thread
        );
        var threads = Execute(result);

        threads.Should().HaveCount(2);
        threads.Select(t => t.Title).Should().Contain("Cat lovers").And.Contain("Dog lovers");
    }

    [Fact]
    public void OrNode_WithAndInsideOrGroup()
    {
        CreateThread("Cat and dog lovers", postContents: ["Body"]);
        CreateThread("Bird watchers", postContents: ["Body"]);
        CreateThread("Fish keepers", postContents: ["Body"]);

        var result = MakeResult(
            new OrNode(new List<SearchNode>
            {
                new AndNode(new List<SearchNode>
                {
                    new KeywordNode("cat"),
                    new KeywordNode("dog")
                }),
                new KeywordNode("bird")
            }),
            mode: ForumSearchMode.Thread
        );
        var threads = Execute(result);

        threads.Should().HaveCount(2);
        threads.Select(t => t.Title).Should().Contain("Cat and dog lovers").And.Contain("Bird watchers");
    }

    #endregion

    #region Author Filter

    [Fact]
    public void AuthorFilter_MatchesThreadAuthor()
    {
        CreateThread("Alice's thread", author: _user1);
        CreateThread("Bob's thread", author: _user2);

        var result = MakeResult(new AuthorFilterNode("alice"));
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Alice's thread");
    }

    [Fact]
    public void AuthorFilter_Negated_ExcludesAuthor()
    {
        CreateThread("Alice's thread", author: _user1);
        CreateThread("Bob's thread", author: _user2);

        var result = MakeResult(new AuthorFilterNode("alice") { Negated = true });
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Bob's thread");
    }

    [Fact]
    public void AuthorFilter_PostMode_MatchesPostAuthor()
    {
        var t1 = CreateThread("Thread by Alice", author: _user1);
        AddPost(t1, "Post by Bob", _user2);

        var t2 = CreateThread("Thread by Alice 2", author: _user1);
        AddPost(t2, "Post by Alice", _user1);

        var result = MakeResult(
            new AuthorFilterNode("bob"),
            mode: ForumSearchMode.Post
        );
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Thread by Alice");
    }

    #endregion

    #region Category Filter

    [Fact]
    public void CategoryFilter_MatchesCategorySlug()
    {
        CreateThread("General thread", category: _generalCategory);
        CreateThread("News thread", category: _newsCategory);

        var result = MakeResult(new ForumCategoryFilterNode("gen"));
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("General thread");
    }

    [Fact]
    public void CategoryFilter_Negated_ExcludesCategory()
    {
        CreateThread("General thread", category: _generalCategory);
        CreateThread("News thread", category: _newsCategory);

        var result = MakeResult(new ForumCategoryFilterNode("gen") { Negated = true });
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("News thread");
    }

    #endregion

    #region Status Filters

    [Fact]
    public void StatusFilter_Pinned_ReturnsOnlyPinned()
    {
        CreateThread("Pinned thread", isPinned: true);
        CreateThread("Normal thread", isPinned: false);

        var result = MakeResult(new ForumStatusFilterNode("pinned"));
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Pinned thread");
    }

    [Fact]
    public void StatusFilter_Locked_ReturnsOnlyLocked()
    {
        CreateThread("Locked thread", isLocked: true);
        CreateThread("Normal thread", isLocked: false);

        var result = MakeResult(new ForumStatusFilterNode("locked"));
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Locked thread");
    }

    [Fact]
    public void StatusFilter_Negated_ExcludesPinned()
    {
        CreateThread("Pinned thread", isPinned: true);
        CreateThread("Normal thread", isPinned: false);

        var result = MakeResult(new ForumStatusFilterNode("pinned") { Negated = true });
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Normal thread");
    }

    #endregion

    #region Numeric Filters

    [Fact]
    public void NumericFilter_Replies_GreaterThanOrEqual()
    {
        CreateThread("Low replies", replyCount: 2);
        CreateThread("High replies", replyCount: 15);
        CreateThread("Medium replies", replyCount: 10);

        var result = MakeResult(new NumericRangeFilterNode("replies", min: 10, max: null));
        var threads = Execute(result);

        threads.Should().HaveCount(2);
        threads.Select(t => t.Title).Should().Contain("High replies").And.Contain("Medium replies");
    }

    [Fact]
    public void NumericFilter_Views_Range()
    {
        CreateThread("Low views", viewCount: 5);
        CreateThread("Medium views", viewCount: 50);
        CreateThread("High views", viewCount: 500);

        var result = MakeResult(new NumericRangeFilterNode("views", min: 10, max: 100));
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Medium views");
    }

    [Fact]
    public void NumericFilter_Negated_ExcludesRange()
    {
        CreateThread("Low replies", replyCount: 2);
        CreateThread("High replies", replyCount: 15);

        var result = MakeResult(new NumericRangeFilterNode("replies", min: 10, max: null) { Negated = true });
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Low replies");
    }

    #endregion

    #region Date Filters

    [Fact]
    public void DateFilter_CreatedAt_Min()
    {
        CreateThread("Old thread", createdAt: new DateTime(2023, 1, 1));
        CreateThread("New thread", createdAt: new DateTime(2025, 6, 1));

        var result = MakeResult(new ForumDateRangeFilterNode("created", min: new DateTime(2025, 1, 1)));
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("New thread");
    }

    [Fact]
    public void DateFilter_Activity_Max()
    {
        CreateThread("Old activity", lastPostAt: new DateTime(2023, 6, 1));
        CreateThread("Recent activity", lastPostAt: new DateTime(2025, 6, 1));

        var result = MakeResult(new ForumDateRangeFilterNode("activity", max: new DateTime(2024, 1, 1)));
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Old activity");
    }

    [Fact]
    public void DateFilter_CreatedAt_Range()
    {
        CreateThread("Before range", createdAt: new DateTime(2023, 1, 1));
        CreateThread("In range", createdAt: new DateTime(2024, 6, 15));
        CreateThread("After range", createdAt: new DateTime(2026, 1, 1));

        var result = MakeResult(new ForumDateRangeFilterNode("created",
            min: new DateTime(2024, 1, 1),
            max: new DateTime(2025, 1, 1)));
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("In range");
    }

    [Fact]
    public void DateFilter_Negated_ExcludesRange()
    {
        CreateThread("In range", createdAt: new DateTime(2024, 6, 15));
        CreateThread("Outside range", createdAt: new DateTime(2023, 1, 1));

        var result = MakeResult(new ForumDateRangeFilterNode("created",
            min: new DateTime(2024, 1, 1),
            max: new DateTime(2025, 1, 1)) { Negated = true });
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Outside range");
    }

    #endregion

    #region Ordering

    [Fact]
    public void DefaultOrdering_LastPostAtDescending()
    {
        CreateThread("Old activity", lastPostAt: new DateTime(2023, 1, 1));
        CreateThread("New activity", lastPostAt: new DateTime(2025, 1, 1));
        CreateThread("Mid activity", lastPostAt: new DateTime(2024, 1, 1));

        var result = MakeResult();
        var threads = Execute(result);

        threads.Should().HaveCount(3);
        threads[0].Title.Should().Be("New activity");
        threads[1].Title.Should().Be("Mid activity");
        threads[2].Title.Should().Be("Old activity");
    }

    [Fact]
    public void OrderBy_Date_Ascending()
    {
        CreateThread("Old", createdAt: new DateTime(2023, 1, 1));
        CreateThread("New", createdAt: new DateTime(2025, 1, 1));

        var result = MakeResult(orderBy: new OrderByNode("date", descending: false));
        var threads = Execute(result);

        threads.Should().HaveCount(2);
        threads[0].Title.Should().Be("Old");
        threads[1].Title.Should().Be("New");
    }

    [Fact]
    public void OrderBy_Replies_Descending()
    {
        CreateThread("Few replies", replyCount: 2);
        CreateThread("Many replies", replyCount: 50);
        CreateThread("Some replies", replyCount: 10);

        var result = MakeResult(orderBy: new OrderByNode("replies", descending: true));
        var threads = Execute(result);

        threads.Should().HaveCount(3);
        threads[0].Title.Should().Be("Many replies");
        threads[1].Title.Should().Be("Some replies");
        threads[2].Title.Should().Be("Few replies");
    }

    [Fact]
    public void OrderBy_Views_Ascending()
    {
        CreateThread("High views", viewCount: 500);
        CreateThread("Low views", viewCount: 5);
        CreateThread("Mid views", viewCount: 50);

        var result = MakeResult(orderBy: new OrderByNode("views", descending: false));
        var threads = Execute(result);

        threads.Should().HaveCount(3);
        threads[0].Title.Should().Be("Low views");
        threads[1].Title.Should().Be("Mid views");
        threads[2].Title.Should().Be("High views");
    }

    [Fact]
    public void OrderBy_Activity_Descending()
    {
        CreateThread("Old", lastPostAt: new DateTime(2023, 1, 1));
        CreateThread("New", lastPostAt: new DateTime(2025, 1, 1));

        var result = MakeResult(orderBy: new OrderByNode("activity", descending: true));
        var threads = Execute(result);

        threads.Should().HaveCount(2);
        threads[0].Title.Should().Be("New");
        threads[1].Title.Should().Be("Old");
    }

    #endregion

    #region Count Query

    [Fact]
    public void CountQuery_ReturnsCorrectCount()
    {
        CreateThread("Cat thread", postContents: ["Cat content"]);
        CreateThread("Dog thread", postContents: ["Dog content"]);
        CreateThread("Bird thread", postContents: ["Bird content"]);

        var result = MakeResult(new KeywordNode("cat"), mode: ForumSearchMode.Thread);
        var count = ExecuteCount(result);

        count.Should().Be(1);
    }

    #endregion

    #region Complex Queries

    [Fact]
    public void Complex_KeywordWithAuthorAndCategory()
    {
        CreateThread("Cat help by Alice", author: _user1, category: _generalCategory, postContents: ["Body"]);
        CreateThread("Cat help by Bob", author: _user2, category: _generalCategory, postContents: ["Body"]);
        CreateThread("Cat news by Alice", author: _user1, category: _newsCategory, postContents: ["Body"]);
        CreateThread("Dog help by Alice", author: _user1, category: _generalCategory, postContents: ["Body"]);

        var result = MakeResult(
            new AndNode(new List<SearchNode>
            {
                new KeywordNode("cat"),
                new AuthorFilterNode("alice"),
                new ForumCategoryFilterNode("gen")
            }),
            mode: ForumSearchMode.Thread
        );
        var threads = Execute(result);

        threads.Should().HaveCount(1);
        threads[0].Title.Should().Be("Cat help by Alice");
    }

    [Fact]
    public void Complex_OrWithStatusFilter()
    {
        CreateThread("Pinned cat thread", isPinned: true, postContents: ["Cat"]);
        CreateThread("Normal cat thread", isPinned: false, postContents: ["Cat"]);
        CreateThread("Pinned dog thread", isPinned: true, postContents: ["Dog"]);

        var result = MakeResult(
            new AndNode(new List<SearchNode>
            {
                new OrNode(new List<SearchNode>
                {
                    new KeywordNode("cat"),
                    new KeywordNode("dog")
                }),
                new ForumStatusFilterNode("pinned")
            }),
            mode: ForumSearchMode.Thread
        );
        var threads = Execute(result);

        threads.Should().HaveCount(2);
        threads.Select(t => t.Title).Should()
            .Contain("Pinned cat thread")
            .And.Contain("Pinned dog thread");
    }

    [Fact]
    public void Complex_NegatedKeywordWithDateAndOrdering()
    {
        CreateThread("Good thread", createdAt: new DateTime(2025, 1, 1), replyCount: 10);
        CreateThread("Spam thread", createdAt: new DateTime(2025, 2, 1), replyCount: 5);
        CreateThread("Another good thread", createdAt: new DateTime(2025, 3, 1), replyCount: 20);

        var result = MakeResult(
            new AndNode(new List<SearchNode>
            {
                new KeywordNode("spam", negated: true),
                new ForumDateRangeFilterNode("created", min: new DateTime(2025, 1, 1))
            }),
            orderBy: new OrderByNode("replies", descending: true),
            mode: ForumSearchMode.Thread
        );
        var threads = Execute(result);

        threads.Should().HaveCount(2);
        threads[0].Title.Should().Be("Another good thread");
        threads[1].Title.Should().Be("Good thread");
    }

    #endregion

    #region Case Insensitivity

    [Fact]
    public void Keyword_IsCaseInsensitive()
    {
        CreateThread("CAT LOVERS UNITE", postContents: ["Body"]);

        var result = MakeResult(new KeywordNode("cat"), mode: ForumSearchMode.Thread);
        var threads = Execute(result);

        threads.Should().HaveCount(1);
    }

    [Fact]
    public void AuthorFilter_IsCaseInsensitive()
    {
        CreateThread("Thread by alice", author: _user1);

        var result = MakeResult(new AuthorFilterNode("ALICE"));
        var threads = Execute(result);

        threads.Should().HaveCount(1);
    }

    #endregion

    #region Empty/Null Root Node

    [Fact]
    public void NullRootNode_ReturnsAllThreadsOrdered()
    {
        CreateThread("A", lastPostAt: new DateTime(2023, 1, 1));
        CreateThread("B", lastPostAt: new DateTime(2025, 1, 1));

        var result = MakeResult();
        var threads = Execute(result);

        threads.Should().HaveCount(2);
        threads[0].Title.Should().Be("B");
    }

    #endregion
}

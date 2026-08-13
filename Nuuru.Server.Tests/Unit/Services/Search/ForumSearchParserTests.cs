using FluentAssertions;
using Nuuru.Server.Services.Search.Forum;
using Nuuru.Server.Services.Search.Nodes;
using Nuuru.Server.Services.Search.Tokens;
using ForumCategoryFilterNode = Nuuru.Server.Services.Search.Forum.CategoryFilterNode;

namespace Nuuru.Server.Tests.Unit.Services.Search;

public class ForumSearchParserTests
{
    #region Keyword Parsing

    [Fact]
    public void Parse_SingleKeyword_ReturnsKeywordNode()
    {
        var tokens = new List<SearchToken> { new TagToken("hello") };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<KeywordNode>();
        var node = (KeywordNode)result.RootNode!;
        node.Keyword.Should().Be("hello");
        node.Negated.Should().BeFalse();
    }

    [Fact]
    public void Parse_NegatedKeyword_ReturnsNegatedKeywordNode()
    {
        var tokens = new List<SearchToken> { new NegatedTagToken("spam") };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<KeywordNode>();
        var node = (KeywordNode)result.RootNode!;
        node.Keyword.Should().Be("spam");
        node.Negated.Should().BeTrue();
    }

    [Fact]
    public void Parse_MultipleKeywords_ReturnsAndNode()
    {
        var tokens = new List<SearchToken>
        {
            new TagToken("hello"),
            new TagToken("world")
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<AndNode>();
        var andNode = (AndNode)result.RootNode!;
        andNode.Children.Should().HaveCount(2);
        andNode.Children[0].Should().BeOfType<KeywordNode>().Which.Keyword.Should().Be("hello");
        andNode.Children[1].Should().BeOfType<KeywordNode>().Which.Keyword.Should().Be("world");
    }

    [Fact]
    public void Parse_WildcardKeyword_ReturnsWildcardKeywordNode()
    {
        var tokens = new List<SearchToken> { new WildcardTagToken("hel", false) };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<WildcardKeywordNode>();
        var node = (WildcardKeywordNode)result.RootNode!;
        node.Prefix.Should().Be("hel");
        node.Negated.Should().BeFalse();
    }

    #endregion

    #region OR Groups

    [Fact]
    public void Parse_OrGroup_ReturnsOrNode()
    {
        var tokens = new List<SearchToken>
        {
            new OrGroupStartToken(),
            new TagToken("hello"),
            new OrSeparatorToken(),
            new TagToken("world"),
            new OrGroupEndToken()
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<OrNode>();
        var orNode = (OrNode)result.RootNode!;
        orNode.Children.Should().HaveCount(2);
        orNode.Children[0].Should().BeOfType<KeywordNode>().Which.Keyword.Should().Be("hello");
        orNode.Children[1].Should().BeOfType<KeywordNode>().Which.Keyword.Should().Be("world");
    }

    [Fact]
    public void Parse_EmptyOrGroup_ReturnsNullWithWarning()
    {
        var tokens = new List<SearchToken>
        {
            new OrGroupStartToken(),
            new OrGroupEndToken()
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeNull();
        result.Warnings.Should().Contain(w => w.Contains("Empty OR group"));
    }

    #endregion

    #region Author Filter

    [Fact]
    public void Parse_Author_ReturnsAuthorFilterNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("author", MetaOperator.Equals, "admin", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<AuthorFilterNode>();
        var node = (AuthorFilterNode)result.RootNode!;
        node.Username.Should().Be("admin");
        node.Negated.Should().BeFalse();
    }

    [Fact]
    public void Parse_UserAlias_ReturnsAuthorFilterNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("user", MetaOperator.Equals, "testuser", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<AuthorFilterNode>();
        ((AuthorFilterNode)result.RootNode!).Username.Should().Be("testuser");
    }

    [Fact]
    public void Parse_NegatedAuthor_HasNegatedFlag()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("author", MetaOperator.Equals, "spammer", true)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<AuthorFilterNode>();
        ((AuthorFilterNode)result.RootNode!).Negated.Should().BeTrue();
    }

    #endregion

    #region Category Filter

    [Fact]
    public void Parse_Category_ReturnsCategoryFilterNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("category", MetaOperator.Equals, "gen", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<ForumCategoryFilterNode>();
        var node = (ForumCategoryFilterNode)result.RootNode!;
        node.CategorySlug.Should().Be("gen");
    }

    [Fact]
    public void Parse_CatAlias_ReturnsCategoryFilterNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("cat", MetaOperator.Equals, "meta", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<ForumCategoryFilterNode>();
        ((ForumCategoryFilterNode)result.RootNode!).CategorySlug.Should().Be("meta");
    }

    #endregion

    #region Numeric Filters

    [Fact]
    public void Parse_RepliesEquals_ReturnsNumericRangeFilterNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("replies", MetaOperator.Equals, "10", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<NumericRangeFilterNode>();
        var node = (NumericRangeFilterNode)result.RootNode!;
        node.Field.Should().Be("replies");
        node.Min.Should().Be(10);
        node.Max.Should().Be(10);
    }

    [Fact]
    public void Parse_RepliesGreaterThan_ReturnsCorrectRange()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("replies", MetaOperator.GreaterThan, "5", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<NumericRangeFilterNode>();
        var node = (NumericRangeFilterNode)result.RootNode!;
        node.Field.Should().Be("replies");
        node.Min.Should().Be(6); // > 5 means >= 6
        node.Max.Should().BeNull();
    }

    [Fact]
    public void Parse_ViewsRange_ReturnsCorrectMinMax()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("views", MetaOperator.Range, "100..500", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<NumericRangeFilterNode>();
        var node = (NumericRangeFilterNode)result.RootNode!;
        node.Field.Should().Be("views");
        node.Min.Should().Be(100);
        node.Max.Should().Be(500);
    }

    [Fact]
    public void Parse_InvalidNumericValue_AddsWarning()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("replies", MetaOperator.Equals, "notanumber", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeNull();
        result.Warnings.Should().Contain(w => w.Contains("Invalid numeric value"));
    }

    #endregion

    #region Date Filters

    [Fact]
    public void Parse_CreatedDate_ReturnsForumDateRangeFilterNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("date", MetaOperator.Equals, "2024-06-15", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<ForumDateRangeFilterNode>();
        var node = (ForumDateRangeFilterNode)result.RootNode!;
        node.Field.Should().Be("created");
        node.Min.Should().Be(new DateTime(2024, 6, 15));
        node.Max.Should().Be(new DateTime(2024, 6, 15).AddDays(1).AddTicks(-1));
    }

    [Fact]
    public void Parse_CreatedAlias_ReturnsCreatedField()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("created", MetaOperator.GreaterThanOrEqual, "2024-01-01", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<ForumDateRangeFilterNode>();
        ((ForumDateRangeFilterNode)result.RootNode!).Field.Should().Be("created");
    }

    [Fact]
    public void Parse_ActivityDate_ReturnsActivityField()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("activity", MetaOperator.Equals, "2024-06-15", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<ForumDateRangeFilterNode>();
        ((ForumDateRangeFilterNode)result.RootNode!).Field.Should().Be("activity");
    }

    [Fact]
    public void Parse_UpdatedAlias_ReturnsActivityField()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("updated", MetaOperator.Equals, "2024-06-15", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<ForumDateRangeFilterNode>();
        ((ForumDateRangeFilterNode)result.RootNode!).Field.Should().Be("activity");
    }

    [Fact]
    public void Parse_DateRange_ReturnsMinAndMax()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("date", MetaOperator.Range, "2024-01-01..2024-12-31", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<ForumDateRangeFilterNode>();
        var node = (ForumDateRangeFilterNode)result.RootNode!;
        node.Min.Should().Be(new DateTime(2024, 1, 1));
        node.Max.Should().Be(new DateTime(2024, 12, 31).AddDays(1).AddTicks(-1));
    }

    [Fact]
    public void Parse_InvalidDate_AddsWarning()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("date", MetaOperator.Equals, "notadate", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeNull();
        result.Warnings.Should().Contain(w => w.Contains("Invalid date"));
    }

    #endregion

    #region Status Filters (is:)

    [Fact]
    public void Parse_IsThread_SetsSearchModeThread()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("is", MetaOperator.Equals, "thread", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.SearchMode.Should().Be(ForumSearchMode.Thread);
        result.RootNode.Should().BeNull(); // SearchModeNode is extracted, not a filter
    }

    [Fact]
    public void Parse_IsPost_SetsSearchModePost()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("is", MetaOperator.Equals, "post", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.SearchMode.Should().Be(ForumSearchMode.Post);
    }

    [Fact]
    public void Parse_IsReply_SetsSearchModePost()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("is", MetaOperator.Equals, "reply", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.SearchMode.Should().Be(ForumSearchMode.Post);
    }

    [Fact]
    public void Parse_DefaultSearchMode_IsAll()
    {
        var tokens = new List<SearchToken>
        {
            new TagToken("hello")
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.SearchMode.Should().Be(ForumSearchMode.All);
    }

    [Fact]
    public void Parse_IsPinned_ReturnsForumStatusFilterNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("is", MetaOperator.Equals, "pinned", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<ForumStatusFilterNode>();
        ((ForumStatusFilterNode)result.RootNode!).Status.Should().Be("pinned");
    }

    [Fact]
    public void Parse_IsLocked_ReturnsForumStatusFilterNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("is", MetaOperator.Equals, "locked", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<ForumStatusFilterNode>();
        ((ForumStatusFilterNode)result.RootNode!).Status.Should().Be("locked");
    }

    [Fact]
    public void Parse_NegatedIsPinned_HasNegatedFlag()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("is", MetaOperator.Equals, "pinned", true)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<ForumStatusFilterNode>();
        ((ForumStatusFilterNode)result.RootNode!).Negated.Should().BeTrue();
    }

    [Fact]
    public void Parse_InvalidIsValue_AddsWarning()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("is", MetaOperator.Equals, "unknown", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeNull();
        result.Warnings.Should().Contain(w => w.Contains("Unknown status"));
    }

    #endregion

    #region Order By

    [Fact]
    public void Parse_OrderByDate_ReturnsOrderByNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("order", MetaOperator.Equals, "date", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.OrderBy.Should().NotBeNull();
        result.OrderBy!.Field.Should().Be("date");
        result.OrderBy.Descending.Should().BeTrue();
    }

    [Fact]
    public void Parse_OrderByActivity_ReturnsActivityField()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("order", MetaOperator.Equals, "activity", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.OrderBy.Should().NotBeNull();
        result.OrderBy!.Field.Should().Be("activity");
    }

    [Fact]
    public void Parse_OrderByRepliesAsc_ReturnsAscending()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("sort", MetaOperator.Equals, "replies_asc", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.OrderBy.Should().NotBeNull();
        result.OrderBy!.Field.Should().Be("replies");
        result.OrderBy.Descending.Should().BeFalse();
    }

    [Fact]
    public void Parse_OrderByViewsDesc_ReturnsDescending()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("order", MetaOperator.Equals, "views_desc", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.OrderBy.Should().NotBeNull();
        result.OrderBy!.Field.Should().Be("views");
        result.OrderBy.Descending.Should().BeTrue();
    }

    [Fact]
    public void Parse_InvalidOrderField_AddsWarning()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("order", MetaOperator.Equals, "score", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.OrderBy.Should().BeNull();
        result.Warnings.Should().Contain(w => w.Contains("Unknown sort field"));
    }

    #endregion

    #region Unknown Meta Tags

    [Fact]
    public void Parse_UnknownMetaTag_AddsWarning()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("character", MetaOperator.Equals, "naruto", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeNull();
        result.Warnings.Should().Contain(w => w.Contains("Unknown filter"));
    }

    #endregion

    #region Complex Queries

    [Fact]
    public void Parse_KeywordsWithFiltersAndOrder_ParsesAllCorrectly()
    {
        var tokens = new List<SearchToken>
        {
            new TagToken("hello"),
            new NegatedTagToken("spam"),
            new MetaTagToken("author", MetaOperator.Equals, "admin", false),
            new MetaTagToken("is", MetaOperator.Equals, "thread", false),
            new MetaTagToken("order", MetaOperator.Equals, "replies", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<AndNode>();
        var andNode = (AndNode)result.RootNode!;
        andNode.Children.Should().HaveCount(3);
        andNode.Children[0].Should().BeOfType<KeywordNode>().Which.Keyword.Should().Be("hello");
        andNode.Children[1].Should().BeOfType<KeywordNode>().Which.Negated.Should().BeTrue();
        andNode.Children[2].Should().BeOfType<AuthorFilterNode>();

        result.SearchMode.Should().Be(ForumSearchMode.Thread);
        result.OrderBy.Should().NotBeNull();
        result.OrderBy!.Field.Should().Be("replies");
    }

    [Fact]
    public void Parse_EmptyTokenList_ReturnsNullRootAndDefaultMode()
    {
        var tokens = new List<SearchToken>();
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeNull();
        result.OrderBy.Should().BeNull();
        result.SearchMode.Should().Be(ForumSearchMode.All);
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Parse_StatusAlias_RoutesToIsOrStatus()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("status", MetaOperator.Equals, "locked", false)
        };
        var parser = new ForumSearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<ForumStatusFilterNode>();
        ((ForumStatusFilterNode)result.RootNode!).Status.Should().Be("locked");
    }

    #endregion
}

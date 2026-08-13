using FluentAssertions;
using Nuuru.Server.Models.Booru;
using Nuuru.Server.Services.Search;
using Nuuru.Server.Services.Search.Nodes;
using Nuuru.Server.Services.Search.Tokens;

namespace Nuuru.Server.Tests.Unit.Services.Search;

public class SearchParserTests
{
    #region Tag Parsing

    [Fact]
    public void Parse_SingleTag_ReturnsTagNode()
    {
        var tokens = new List<SearchToken> { new TagToken("cat") };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<TagNode>();
        var tagNode = (TagNode)result.RootNode!;
        tagNode.Name.Should().Be("cat");
        tagNode.Negated.Should().BeFalse();
    }

    [Fact]
    public void Parse_NegatedTag_ReturnsNegatedTagNode()
    {
        var tokens = new List<SearchToken> { new NegatedTagToken("dog") };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<TagNode>();
        var tagNode = (TagNode)result.RootNode!;
        tagNode.Name.Should().Be("dog");
        tagNode.Negated.Should().BeTrue();
    }

    [Fact]
    public void Parse_MultipleTags_ReturnsAndNode()
    {
        var tokens = new List<SearchToken>
        {
            new TagToken("cat"),
            new TagToken("dog")
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<AndNode>();
        var andNode = (AndNode)result.RootNode!;
        andNode.Children.Should().HaveCount(2);
        andNode.Children[0].Should().BeOfType<TagNode>().Which.Name.Should().Be("cat");
        andNode.Children[1].Should().BeOfType<TagNode>().Which.Name.Should().Be("dog");
    }

    [Fact]
    public void Parse_Wildcard_ReturnsWildcardNode()
    {
        var tokens = new List<SearchToken> { new WildcardTagToken("cat", false) };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<WildcardTagNode>();
        var wildcardNode = (WildcardTagNode)result.RootNode!;
        wildcardNode.Prefix.Should().Be("cat");
        wildcardNode.Negated.Should().BeFalse();
    }

    [Fact]
    public void Parse_NegatedWildcard_ReturnsNegatedWildcardNode()
    {
        var tokens = new List<SearchToken> { new WildcardTagToken("dog", true) };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<WildcardTagNode>();
        var wildcardNode = (WildcardTagNode)result.RootNode!;
        wildcardNode.Prefix.Should().Be("dog");
        wildcardNode.Negated.Should().BeTrue();
    }

    #endregion

    #region OR Groups

    [Fact]
    public void Parse_OrGroup_ReturnsOrNode()
    {
        var tokens = new List<SearchToken>
        {
            new OrGroupStartToken(),
            new TagToken("cat"),
            new OrSeparatorToken(),
            new TagToken("dog"),
            new OrGroupEndToken()
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<OrNode>();
        var orNode = (OrNode)result.RootNode!;
        orNode.Children.Should().HaveCount(2);
        orNode.Children[0].Should().BeOfType<TagNode>().Which.Name.Should().Be("cat");
        orNode.Children[1].Should().BeOfType<TagNode>().Which.Name.Should().Be("dog");
    }

    [Fact]
    public void Parse_OrGroupWithThreeOptions_ReturnsOrNodeWithThreeChildren()
    {
        var tokens = new List<SearchToken>
        {
            new OrGroupStartToken(),
            new TagToken("cat"),
            new OrSeparatorToken(),
            new TagToken("dog"),
            new OrSeparatorToken(),
            new TagToken("bird"),
            new OrGroupEndToken()
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<OrNode>();
        var orNode = (OrNode)result.RootNode!;
        orNode.Children.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_EmptyOrGroup_ReturnsNullWithWarning()
    {
        var tokens = new List<SearchToken>
        {
            new OrGroupStartToken(),
            new OrGroupEndToken()
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeNull();
        result.Warnings.Should().Contain(w => w.Contains("Empty OR group"));
    }

    [Fact]
    public void Parse_UnmatchedOrGroupEnd_AddsWarning()
    {
        var tokens = new List<SearchToken>
        {
            new TagToken("cat"),
            new OrGroupEndToken()
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.Warnings.Should().Contain(w => w.Contains("Unexpected '}'"));
    }

    [Fact]
    public void Parse_UnmatchedOrSeparator_AddsWarning()
    {
        var tokens = new List<SearchToken>
        {
            new TagToken("cat"),
            new OrSeparatorToken(),
            new TagToken("dog")
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.Warnings.Should().Contain(w => w.Contains("Unexpected '~'"));
    }

    #endregion

    #region Meta Tags - Rating

    [Fact]
    public void Parse_RatingSafe_ReturnsRatingFilterNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("rating", MetaOperator.Equals, "safe", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<RatingFilterNode>();
        var ratingNode = (RatingFilterNode)result.RootNode!;
        ratingNode.Rating.Should().Be(PostRating.Safe);
        ratingNode.Negated.Should().BeFalse();
    }

    [Fact]
    public void Parse_RatingQuestionable_ReturnsCorrectRating()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("rating", MetaOperator.Equals, "questionable", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<RatingFilterNode>();
        ((RatingFilterNode)result.RootNode!).Rating.Should().Be(PostRating.Questionable);
    }

    [Fact]
    public void Parse_RatingExplicit_ReturnsCorrectRating()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("rating", MetaOperator.Equals, "explicit", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<RatingFilterNode>();
        ((RatingFilterNode)result.RootNode!).Rating.Should().Be(PostRating.Explicit);
    }

    [Fact]
    public void Parse_RatingShorthand_S_ReturnsSafe()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("rating", MetaOperator.Equals, "s", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<RatingFilterNode>();
        ((RatingFilterNode)result.RootNode!).Rating.Should().Be(PostRating.Safe);
    }

    [Fact]
    public void Parse_RatingShorthand_Q_ReturnsQuestionable()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("r", MetaOperator.Equals, "q", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<RatingFilterNode>();
        ((RatingFilterNode)result.RootNode!).Rating.Should().Be(PostRating.Questionable);
    }

    [Fact]
    public void Parse_RatingShorthand_E_ReturnsExplicit()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("rating", MetaOperator.Equals, "e", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<RatingFilterNode>();
        ((RatingFilterNode)result.RootNode!).Rating.Should().Be(PostRating.Explicit);
    }

    [Fact]
    public void Parse_NegatedRating_HasNegatedFlag()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("rating", MetaOperator.Equals, "safe", true)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<RatingFilterNode>();
        ((RatingFilterNode)result.RootNode!).Negated.Should().BeTrue();
    }

    [Fact]
    public void Parse_InvalidRating_AddsWarning()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("rating", MetaOperator.Equals, "invalid", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeNull();
        result.Warnings.Should().Contain(w => w.Contains("Unknown rating"));
    }

    #endregion

    #region Meta Tags - Uploader

    [Fact]
    public void Parse_Uploader_ReturnsUploaderFilterNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("uploader", MetaOperator.Equals, "admin", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<UploaderFilterNode>();
        var uploaderNode = (UploaderFilterNode)result.RootNode!;
        uploaderNode.Username.Should().Be("admin");
        uploaderNode.Negated.Should().BeFalse();
    }

    [Fact]
    public void Parse_UserAlias_ReturnsUploaderFilterNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("user", MetaOperator.Equals, "testuser", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<UploaderFilterNode>();
        ((UploaderFilterNode)result.RootNode!).Username.Should().Be("testuser");
    }

    #endregion

    #region Meta Tags - Numeric Filters

    [Fact]
    public void Parse_IdEquals_ReturnsNumericRangeFilterNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("id", MetaOperator.Equals, "100", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<NumericRangeFilterNode>();
        var numericNode = (NumericRangeFilterNode)result.RootNode!;
        numericNode.Field.Should().Be("id");
        numericNode.Min.Should().Be(100);
        numericNode.Max.Should().Be(100);
    }

    [Fact]
    public void Parse_IdGreaterThan_ReturnsCorrectRange()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("id", MetaOperator.GreaterThan, "100", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<NumericRangeFilterNode>();
        var numericNode = (NumericRangeFilterNode)result.RootNode!;
        numericNode.Min.Should().Be(101); // > 100 means >= 101
        numericNode.Max.Should().BeNull();
    }

    [Fact]
    public void Parse_IdLessThan_ReturnsCorrectRange()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("id", MetaOperator.LessThan, "100", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<NumericRangeFilterNode>();
        var numericNode = (NumericRangeFilterNode)result.RootNode!;
        numericNode.Min.Should().BeNull();
        numericNode.Max.Should().Be(99); // < 100 means <= 99
    }

    [Fact]
    public void Parse_IdRange_ReturnsCorrectMinMax()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("id", MetaOperator.Range, "100..200", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<NumericRangeFilterNode>();
        var numericNode = (NumericRangeFilterNode)result.RootNode!;
        numericNode.Min.Should().Be(100);
        numericNode.Max.Should().Be(200);
    }

    [Fact]
    public void Parse_WidthFilter_ReturnsNumericRangeForWidth()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("width", MetaOperator.GreaterThanOrEqual, "1920", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<NumericRangeFilterNode>();
        var numericNode = (NumericRangeFilterNode)result.RootNode!;
        numericNode.Field.Should().Be("width");
        numericNode.Min.Should().Be(1920);
    }

    [Fact]
    public void Parse_HeightFilter_ReturnsNumericRangeForHeight()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("height", MetaOperator.LessThanOrEqual, "1080", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<NumericRangeFilterNode>();
        var numericNode = (NumericRangeFilterNode)result.RootNode!;
        numericNode.Field.Should().Be("height");
        numericNode.Max.Should().Be(1080);
    }

    [Fact]
    public void Parse_InvalidNumericValue_AddsWarning()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("id", MetaOperator.Equals, "notanumber", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeNull();
        result.Warnings.Should().Contain(w => w.Contains("Invalid numeric value"));
    }

    #endregion

    #region Meta Tags - File Size

    [Fact]
    public void Parse_FilesizeMB_ParsesCorrectly()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("filesize", MetaOperator.GreaterThan, "1mb", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<NumericRangeFilterNode>();
        var numericNode = (NumericRangeFilterNode)result.RootNode!;
        numericNode.Field.Should().Be("filesize");
        numericNode.Min.Should().Be(1024 * 1024 + 1); // > 1MB
    }

    [Fact]
    public void Parse_FilesizeKB_ParsesCorrectly()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("filesize", MetaOperator.LessThan, "500kb", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<NumericRangeFilterNode>();
        var numericNode = (NumericRangeFilterNode)result.RootNode!;
        numericNode.Field.Should().Be("filesize");
        numericNode.Max.Should().Be(500 * 1024 - 1); // < 500KB
    }

    #endregion

    #region Meta Tags - FileType

    [Fact]
    public void Parse_FiletypePng_ReturnsMimeType()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("filetype", MetaOperator.Equals, "png", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<FileTypeFilterNode>();
        var fileTypeNode = (FileTypeFilterNode)result.RootNode!;
        fileTypeNode.MimeType.Should().Be("image/png");
    }

    [Fact]
    public void Parse_FiletypeJpg_ReturnsMimeType()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("type", MetaOperator.Equals, "jpg", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<FileTypeFilterNode>();
        var fileTypeNode = (FileTypeFilterNode)result.RootNode!;
        fileTypeNode.MimeType.Should().Be("image/jpeg");
    }

    [Fact]
    public void Parse_FiletypeWebm_ReturnsMimeType()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("filetype", MetaOperator.Equals, "webm", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<FileTypeFilterNode>();
        var fileTypeNode = (FileTypeFilterNode)result.RootNode!;
        fileTypeNode.MimeType.Should().Be("video/webm");
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
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.OrderBy.Should().NotBeNull();
        result.OrderBy!.Field.Should().Be("date");
        result.OrderBy.Descending.Should().BeTrue();
    }

    [Fact]
    public void Parse_OrderByIdAsc_ReturnsAscendingOrder()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("sort", MetaOperator.Equals, "id_asc", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.OrderBy.Should().NotBeNull();
        result.OrderBy!.Field.Should().Be("id");
        result.OrderBy.Descending.Should().BeFalse();
    }

    [Fact]
    public void Parse_OrderByScore_ReturnsScoreOrder()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("order", MetaOperator.Equals, "score", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.OrderBy.Should().NotBeNull();
        result.OrderBy!.Field.Should().Be("score");
    }

    [Fact]
    public void Parse_OrderByRandom_ReturnsRandomOrder()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("order", MetaOperator.Equals, "random", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.OrderBy.Should().NotBeNull();
        result.OrderBy!.Field.Should().Be("random");
    }

    [Fact]
    public void Parse_InvalidOrderField_AddsWarning()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("order", MetaOperator.Equals, "invalid", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.OrderBy.Should().BeNull();
        result.Warnings.Should().Contain(w => w.Contains("Unknown sort field"));
    }

    #endregion

    #region Category-Prefixed Tags

    /// <summary>
    /// Unknown meta-tags like "character:naruto" should be treated as
    /// category-prefixed tags.
    /// </summary>
    [Fact]
    public void Parse_CategoryPrefixedTag_ReturnsCategoryTagNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("character", MetaOperator.Equals, "naruto", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<CategoryTagNode>();
        var categoryNode = (CategoryTagNode)result.RootNode!;
        categoryNode.CategorySlug.Should().Be("character");
        categoryNode.Name.Should().Be("naruto");
        categoryNode.Negated.Should().BeFalse();
    }

    [Fact]
    public void Parse_ArtistCategoryTag_ReturnsCategoryTagNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("artist", MetaOperator.Equals, "picasso", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<CategoryTagNode>();
        var categoryNode = (CategoryTagNode)result.RootNode!;
        categoryNode.CategorySlug.Should().Be("artist");
        categoryNode.Name.Should().Be("picasso");
    }

    [Fact]
    public void Parse_NegatedCategoryTag_HasNegatedFlag()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("character", MetaOperator.Equals, "naruto", true)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<CategoryTagNode>();
        ((CategoryTagNode)result.RootNode!).Negated.Should().BeTrue();
    }

    [Fact]
    public void Parse_MultipleCategoryTags_ReturnsAndNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("character", MetaOperator.Equals, "naruto", false),
            new MetaTagToken("artist", MetaOperator.Equals, "kishimoto", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<AndNode>();
        var andNode = (AndNode)result.RootNode!;
        andNode.Children.Should().HaveCount(2);
        andNode.Children[0].Should().BeOfType<CategoryTagNode>();
        andNode.Children[1].Should().BeOfType<CategoryTagNode>();
    }

    #endregion

    #region Meta Tags - Date

    [Fact]
    public void Parse_DateEquals_ReturnsDateRangeFilterNode()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("date", MetaOperator.Equals, "2024-06-15", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<DateRangeFilterNode>();
        var dateNode = (DateRangeFilterNode)result.RootNode!;
        dateNode.Min.Should().Be(new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        dateNode.Max.Should().Be(new DateTime(2024, 6, 15, 23, 59, 59, 999, DateTimeKind.Utc).AddTicks(9999));
    }

    [Fact]
    public void Parse_DateRelativeDay_ReturnsCorrectRange()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("date", MetaOperator.GreaterThanOrEqual, "day", false)
        } ;
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<DateRangeFilterNode>();
        var dateNode = (DateRangeFilterNode)result.RootNode!;
        dateNode.Min.Should().BeCloseTo(DateTime.UtcNow.AddDays(-1), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Parse_DateDuration_23h_ReturnsCorrectRange()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("date", MetaOperator.GreaterThanOrEqual, "23h", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<DateRangeFilterNode>();
        var dateNode = (DateRangeFilterNode)result.RootNode!;
        dateNode.Min.Should().BeCloseTo(DateTime.UtcNow.AddHours(-23), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Parse_DateDuration_DefaultToDays_ReturnsCorrectRange()
    {
        var tokens = new List<SearchToken>
        {
            new MetaTagToken("date", MetaOperator.GreaterThanOrEqual, "5", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<DateRangeFilterNode>();
        var dateNode = (DateRangeFilterNode)result.RootNode!;
        dateNode.Min.Should().BeCloseTo(DateTime.UtcNow.AddDays(-5), TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Complex Queries

    [Fact]
    public void Parse_TagsWithOrGroupAndMetaTag_ParsesAllCorrectly()
    {
        var tokens = new List<SearchToken>
        {
            new TagToken("cat"),
            new OrGroupStartToken(),
            new TagToken("dog"),
            new OrSeparatorToken(),
            new TagToken("bird"),
            new OrGroupEndToken(),
            new MetaTagToken("rating", MetaOperator.Equals, "safe", false)
        };
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeOfType<AndNode>();
        var andNode = (AndNode)result.RootNode!;
        andNode.Children.Should().HaveCount(3);
        andNode.Children[0].Should().BeOfType<TagNode>();
        andNode.Children[1].Should().BeOfType<OrNode>();
        andNode.Children[2].Should().BeOfType<RatingFilterNode>();
    }

    [Fact]
    public void Parse_EmptyTokenList_ReturnsNullRootNode()
    {
        var tokens = new List<SearchToken>();
        var parser = new SearchParser(tokens);
        var result = parser.Parse();

        result.RootNode.Should().BeNull();
        result.OrderBy.Should().BeNull();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    #endregion
}

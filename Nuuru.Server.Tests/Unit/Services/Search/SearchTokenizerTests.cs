using FluentAssertions;
using Nuuru.Server.Services.Search;
using Nuuru.Server.Services.Search.Tokens;

namespace Nuuru.Server.Tests.Unit.Services.Search;

public class SearchTokenizerTests
{
    #region Basic Tags

    [Fact]
    public void Tokenize_SingleTag_ReturnsTagToken()
    {
        var tokenizer = new SearchTokenizer("cat");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        tokens[0].Should().BeOfType<TagToken>()
            .Which.Name.Should().Be("cat");
    }

    [Fact]
    public void Tokenize_MultipleTags_ReturnsSeparateTokens()
    {
        var tokenizer = new SearchTokenizer("cat dog");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(2);
        tokens[0].Should().BeOfType<TagToken>().Which.Name.Should().Be("cat");
        tokens[1].Should().BeOfType<TagToken>().Which.Name.Should().Be("dog");
    }

    [Fact]
    public void Tokenize_TagsWithUnderscores_PreservesUnderscores()
    {
        var tokenizer = new SearchTokenizer("big_cat long_tail");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(2);
        tokens[0].Should().BeOfType<TagToken>().Which.Name.Should().Be("big_cat");
        tokens[1].Should().BeOfType<TagToken>().Which.Name.Should().Be("long_tail");
    }

    [Fact]
    public void Tokenize_TagsWithHyphens_PreservesHyphens()
    {
        var tokenizer = new SearchTokenizer("high-quality");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        tokens[0].Should().BeOfType<TagToken>().Which.Name.Should().Be("high-quality");
    }

    [Fact]
    public void Tokenize_Normalizes_ToLowercase()
    {
        var tokenizer = new SearchTokenizer("CAT DoG");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(2);
        tokens[0].Should().BeOfType<TagToken>().Which.Name.Should().Be("cat");
        tokens[1].Should().BeOfType<TagToken>().Which.Name.Should().Be("dog");
    }

    [Fact]
    public void Tokenize_EmptyInput_ReturnsEmptyList()
    {
        var tokenizer = new SearchTokenizer("");
        var tokens = tokenizer.Tokenize();

        tokens.Should().BeEmpty();
    }

    [Fact]
    public void Tokenize_WhitespaceOnly_ReturnsEmptyList()
    {
        var tokenizer = new SearchTokenizer("   \t  \n  ");
        var tokens = tokenizer.Tokenize();

        tokens.Should().BeEmpty();
    }

    #endregion

    #region Negated Tags

    [Fact]
    public void Tokenize_NegatedTag_ReturnsNegatedTagToken()
    {
        var tokenizer = new SearchTokenizer("-cat");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        tokens[0].Should().BeOfType<NegatedTagToken>()
            .Which.Name.Should().Be("cat");
    }

    [Fact]
    public void Tokenize_MixedPositiveAndNegative_ReturnsCorrectTokenTypes()
    {
        var tokenizer = new SearchTokenizer("cat -dog bird");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(3);
        tokens[0].Should().BeOfType<TagToken>().Which.Name.Should().Be("cat");
        tokens[1].Should().BeOfType<NegatedTagToken>().Which.Name.Should().Be("dog");
        tokens[2].Should().BeOfType<TagToken>().Which.Name.Should().Be("bird");
    }

    [Fact]
    public void Tokenize_MultipleNegatedTags_ReturnsAllNegated()
    {
        var tokenizer = new SearchTokenizer("-cat -dog -bird");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(3);
        tokens.Should().AllBeOfType<NegatedTagToken>();
    }

    #endregion

    #region Wildcards

    [Fact]
    public void Tokenize_WildcardTag_ReturnsWildcardToken()
    {
        var tokenizer = new SearchTokenizer("cat*");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        var wildcard = tokens[0].Should().BeOfType<WildcardTagToken>().Subject;
        wildcard.Prefix.Should().Be("cat");
        wildcard.Negated.Should().BeFalse();
    }

    [Fact]
    public void Tokenize_NegatedWildcard_ReturnsNegatedWildcardToken()
    {
        var tokenizer = new SearchTokenizer("-cat*");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        var wildcard = tokens[0].Should().BeOfType<WildcardTagToken>().Subject;
        wildcard.Prefix.Should().Be("cat");
        wildcard.Negated.Should().BeTrue();
    }

    #endregion

    #region OR Groups

    [Fact]
    public void Tokenize_OrGroup_ReturnsGroupTokens()
    {
        var tokenizer = new SearchTokenizer("{cat ~ dog}");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(5);
        tokens[0].Should().BeOfType<OrGroupStartToken>();
        tokens[1].Should().BeOfType<TagToken>().Which.Name.Should().Be("cat");
        tokens[2].Should().BeOfType<OrSeparatorToken>();
        tokens[3].Should().BeOfType<TagToken>().Which.Name.Should().Be("dog");
        tokens[4].Should().BeOfType<OrGroupEndToken>();
    }

    [Fact]
    public void Tokenize_OrGroupNoSpaces_StillParsesCorrectly()
    {
        var tokenizer = new SearchTokenizer("{cat~dog}");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(5);
        tokens[0].Should().BeOfType<OrGroupStartToken>();
        tokens[1].Should().BeOfType<TagToken>().Which.Name.Should().Be("cat");
        tokens[2].Should().BeOfType<OrSeparatorToken>();
        tokens[3].Should().BeOfType<TagToken>().Which.Name.Should().Be("dog");
        tokens[4].Should().BeOfType<OrGroupEndToken>();
    }

    [Fact]
    public void Tokenize_OrGroupWithThreeOptions_ParsesAll()
    {
        var tokenizer = new SearchTokenizer("{cat ~ dog ~ bird}");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(7);
        tokens[0].Should().BeOfType<OrGroupStartToken>();
        tokens[1].Should().BeOfType<TagToken>().Which.Name.Should().Be("cat");
        tokens[2].Should().BeOfType<OrSeparatorToken>();
        tokens[3].Should().BeOfType<TagToken>().Which.Name.Should().Be("dog");
        tokens[4].Should().BeOfType<OrSeparatorToken>();
        tokens[5].Should().BeOfType<TagToken>().Which.Name.Should().Be("bird");
        tokens[6].Should().BeOfType<OrGroupEndToken>();
    }

    #endregion

    #region Meta Tags

    [Fact]
    public void Tokenize_RatingMetaTag_ReturnsMetaTagToken()
    {
        var tokenizer = new SearchTokenizer("rating:safe");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        var meta = tokens[0].Should().BeOfType<MetaTagToken>().Subject;
        meta.Key.Should().Be("rating");
        meta.Value.Should().Be("safe");
        meta.Operator.Should().Be(MetaOperator.Equals);
        meta.Negated.Should().BeFalse();
    }

    [Fact]
    public void Tokenize_NegatedMetaTag_HasNegatedFlag()
    {
        var tokenizer = new SearchTokenizer("-rating:safe");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        var meta = tokens[0].Should().BeOfType<MetaTagToken>().Subject;
        meta.Key.Should().Be("rating");
        meta.Value.Should().Be("safe");
        meta.Negated.Should().BeTrue();
    }

    [Fact]
    public void Tokenize_MetaTagGreaterThan_ParsesOperator()
    {
        var tokenizer = new SearchTokenizer("id:>100");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        var meta = tokens[0].Should().BeOfType<MetaTagToken>().Subject;
        meta.Key.Should().Be("id");
        meta.Operator.Should().Be(MetaOperator.GreaterThan);
        meta.Value.Should().Be("100");
    }

    [Fact]
    public void Tokenize_MetaTagLessThan_ParsesOperator()
    {
        var tokenizer = new SearchTokenizer("id:<100");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        var meta = tokens[0].Should().BeOfType<MetaTagToken>().Subject;
        meta.Key.Should().Be("id");
        meta.Operator.Should().Be(MetaOperator.LessThan);
        meta.Value.Should().Be("100");
    }

    [Fact]
    public void Tokenize_MetaTagGreaterThanOrEqual_ParsesOperator()
    {
        var tokenizer = new SearchTokenizer("width:>=1920");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        var meta = tokens[0].Should().BeOfType<MetaTagToken>().Subject;
        meta.Key.Should().Be("width");
        meta.Operator.Should().Be(MetaOperator.GreaterThanOrEqual);
        meta.Value.Should().Be("1920");
    }

    [Fact]
    public void Tokenize_MetaTagLessThanOrEqual_ParsesOperator()
    {
        var tokenizer = new SearchTokenizer("height:<=1080");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        var meta = tokens[0].Should().BeOfType<MetaTagToken>().Subject;
        meta.Key.Should().Be("height");
        meta.Operator.Should().Be(MetaOperator.LessThanOrEqual);
        meta.Value.Should().Be("1080");
    }

    [Fact]
    public void Tokenize_MetaTagRange_ParsesOperator()
    {
        var tokenizer = new SearchTokenizer("id:100..200");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        var meta = tokens[0].Should().BeOfType<MetaTagToken>().Subject;
        meta.Key.Should().Be("id");
        meta.Operator.Should().Be(MetaOperator.Range);
        meta.Value.Should().Be("100..200");
    }

    [Fact]
    public void Tokenize_FilesizeWithUnit_PreservesValue()
    {
        var tokenizer = new SearchTokenizer("filesize:>1mb");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        var meta = tokens[0].Should().BeOfType<MetaTagToken>().Subject;
        meta.Key.Should().Be("filesize");
        meta.Operator.Should().Be(MetaOperator.GreaterThan);
        meta.Value.Should().Be("1mb");
    }

    #endregion

    #region Category-Prefixed Tags

    /// <summary>
    /// Category-prefixed tags like "character:naruto" are tokenized as MetaTagTokens.
    /// The parser then recognizes unknown meta-tag keys as category slugs and
    /// creates CategoryTagNodes for them.
    /// </summary>
    [Fact]
    public void Tokenize_CategoryTag_TokenizedAsMetaTag()
    {
        var tokenizer = new SearchTokenizer("character:naruto");
        var tokens = tokenizer.Tokenize();

        // Tokenizer produces MetaTagToken, parser converts to CategoryTagNode
        tokens.Should().HaveCount(1);
        tokens[0].Should().BeOfType<MetaTagToken>();

        var meta = (MetaTagToken)tokens[0];
        meta.Key.Should().Be("character");
        meta.Value.Should().Be("naruto");
    }

    [Fact]
    public void Tokenize_ArtistCategoryTag_TokenizedAsMetaTag()
    {
        var tokenizer = new SearchTokenizer("artist:picasso");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        tokens[0].Should().BeOfType<MetaTagToken>();

        var meta = (MetaTagToken)tokens[0];
        meta.Key.Should().Be("artist");
        meta.Value.Should().Be("picasso");
    }

    [Fact]
    public void Tokenize_NegatedCategoryTag_HasNegatedFlag()
    {
        var tokenizer = new SearchTokenizer("-character:naruto");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        var meta = tokens[0].Should().BeOfType<MetaTagToken>().Subject;
        meta.Key.Should().Be("character");
        meta.Value.Should().Be("naruto");
        meta.Negated.Should().BeTrue();
    }

    [Fact]
    public void Tokenize_MetaCategoryTag_TokenizedAsMetaTag()
    {
        var tokenizer = new SearchTokenizer("meta:tagme");
        var tokens = tokenizer.Tokenize();

        // "meta" is both a category name and could be confused with other meta-tags
        tokens.Should().HaveCount(1);
        tokens[0].Should().BeOfType<MetaTagToken>();

        var meta = (MetaTagToken)tokens[0];
        meta.Key.Should().Be("meta");
        meta.Value.Should().Be("tagme");
    }

    #endregion

    #region Quoted Phrases

    [Fact]
    public void Tokenize_QuotedPhrase_ReturnsTagTokenWithSpaces()
    {
        var tokenizer = new SearchTokenizer("\"hello world\"");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        tokens[0].Should().BeOfType<TagToken>()
            .Which.Name.Should().Be("hello world");
    }

    [Fact]
    public void Tokenize_NegatedQuotedPhrase_ReturnsNegatedTagToken()
    {
        var tokenizer = new SearchTokenizer("-\"spam words\"");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        tokens[0].Should().BeOfType<NegatedTagToken>()
            .Which.Name.Should().Be("spam words");
    }

    [Fact]
    public void Tokenize_QuotedPhraseWithOtherTerms_ParsesAll()
    {
        var tokenizer = new SearchTokenizer("cat \"exact phrase\" -dog");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(3);
        tokens[0].Should().BeOfType<TagToken>().Which.Name.Should().Be("cat");
        tokens[1].Should().BeOfType<TagToken>().Which.Name.Should().Be("exact phrase");
        tokens[2].Should().BeOfType<NegatedTagToken>().Which.Name.Should().Be("dog");
    }

    [Fact]
    public void Tokenize_QuotedPhrase_NormalizesToLowercase()
    {
        var tokenizer = new SearchTokenizer("\"Hello World\"");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        tokens[0].Should().BeOfType<TagToken>()
            .Which.Name.Should().Be("hello world");
    }

    [Fact]
    public void Tokenize_UnclosedQuote_ReadsToEnd()
    {
        var tokenizer = new SearchTokenizer("\"unclosed phrase");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        tokens[0].Should().BeOfType<TagToken>()
            .Which.Name.Should().Be("unclosed phrase");
    }

    [Fact]
    public void Tokenize_EmptyQuotedPhrase_ReturnsNoToken()
    {
        var tokenizer = new SearchTokenizer("\"\"");
        var tokens = tokenizer.Tokenize();

        tokens.Should().BeEmpty();
    }

    #endregion

    #region Complex Queries

    [Fact]
    public void Tokenize_ComplexQuery_ParsesAllParts()
    {
        var tokenizer = new SearchTokenizer("cat -dog rating:safe {bird ~ fish}");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(8);
        tokens[0].Should().BeOfType<TagToken>().Which.Name.Should().Be("cat");
        tokens[1].Should().BeOfType<NegatedTagToken>().Which.Name.Should().Be("dog");
        tokens[2].Should().BeOfType<MetaTagToken>();
        tokens[3].Should().BeOfType<OrGroupStartToken>();
        tokens[4].Should().BeOfType<TagToken>().Which.Name.Should().Be("bird");
        tokens[5].Should().BeOfType<OrSeparatorToken>();
        tokens[6].Should().BeOfType<TagToken>().Which.Name.Should().Be("fish");
        tokens[7].Should().BeOfType<OrGroupEndToken>();
    }

    [Fact]
    public void Tokenize_QueryWithExtraWhitespace_HandlesGracefully()
    {
        var tokenizer = new SearchTokenizer("  cat    dog   ");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(2);
        tokens[0].Should().BeOfType<TagToken>().Which.Name.Should().Be("cat");
        tokens[1].Should().BeOfType<TagToken>().Which.Name.Should().Be("dog");
    }

    #endregion
}

using FluentAssertions;
using Nuuru.Server.DTOs.BBCode;
using Nuuru.Server.Services.BBCode;

namespace Nuuru.Server.Tests.Unit.Services.BBCode;

public class BBCodeServiceTests
{
    private readonly BBCodeService _service = new();

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyString()
    {
        var result = _service.Parse("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NullString_ReturnsEmptyString()
    {
        var result = _service.Parse(null!);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_PlainText_ReturnsHtmlEncodedText()
    {
        var result = _service.Parse("Hello world");

        result.Should().Be("Hello world");
    }

    [Fact]
    public void Parse_HtmlInInput_IsEscaped()
    {
        // Using text not at start of line to avoid triggering orangetext
        var result = _service.Parse("test <script>alert('xss')</script>");

        result.Should().NotContain("<script>");
        result.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void Parse_BoldTag_ReturnsStrong()
    {
        var result = _service.Parse("[b]bold[/b]");

        result.Should().Be("<strong>bold</strong>");
    }

    [Fact]
    public void Parse_ItalicTag_ReturnsEm()
    {
        var result = _service.Parse("[i]italic[/i]");

        result.Should().Be("<em>italic</em>");
    }

    [Fact]
    public void Parse_StrikeTag_ReturnsDel()
    {
        var result = _service.Parse("[s]strike[/s]");

        result.Should().Be("<del>strike</del>");
    }

    [Fact]
    public void Parse_UnderlineTag()
    {
        var result = _service.Parse("[u]underline[/u]");

        result.Should().Contain("bbcode-underline");
    }

    [Fact]
    public void Parse_CodeTag()
    {
        var result = _service.Parse("[code]console.log('test')[/code]");

        result.Should().Contain("<pre class=\"bbcode-code\">");
        result.Should().Contain("<code>");
        result.Should().Contain("console.log(&#x27;test&#x27;)");
    }

    [Fact]
    public void Parse_SpoilerTag()
    {
        var result = _service.Parse("[spoiler]hidden[/spoiler]");

        result.Should().Contain("bbcode-spoiler");
        result.Should().Contain("hidden");
    }

    [Fact]
    public void Parse_BlurTag()
    {
        var result = _service.Parse("[blur]blurred[/blur]");

        result.Should().Contain("bbcode-blur");
    }

    [Fact]
    public void Parse_Newlines_ConvertedToBr()
    {
        var result = _service.Parse("Line1\nLine2");

        result.Should().Be("Line1<br>Line2");
    }

    [Fact]
    public void Parse_Greentext()
    {
        var result = _service.Parse(">implying");

        result.Should().Contain("bbcode-greentext");
        result.Should().Contain("implying");
    }

    [Fact]
    public void Parse_Greentext_AfterNewline()
    {
        var result = _service.Parse("normal\n>greentext");

        result.Should().Contain("normal<br>");
        result.Should().Contain("bbcode-greentext");
    }

    [Fact]
    public void Parse_Orangetext()
    {
        var result = _service.Parse("<orange");

        result.Should().Contain("bbcode-orangetext");
    }

    [Fact]
    public void Parse_Redtext()
    {
        var result = _service.Parse("==red==");

        result.Should().Contain("bbcode-redtext");
        result.Should().Contain("red");
    }

    [Fact]
    public void Parse_Bluetext()
    {
        var result = _service.Parse("--blue--");

        result.Should().Contain("bbcode-bluetext");
    }

    [Fact]
    public void Parse_Glow()
    {
        var result = _service.Parse("%%glow%%");

        result.Should().Contain("bbcode-glow");
    }

    [Fact]
    public void Parse_RedGlow()
    {
        var result = _service.Parse("!!red!!");

        result.Should().Contain("bbcode-glow-red");
    }

    [Fact]
    public void Parse_YellowGlow()
    {
        var result = _service.Parse("::yellow::");

        result.Should().Contain("bbcode-glow-yellow");
    }

    [Fact]
    public void Parse_Rainbow()
    {
        var result = _service.Parse("~-~rainbow~-~");

        result.Should().Contain("bbcode-rainbow");
    }

    [Fact]
    public void Parse_UrlWithAttribute()
    {
        var result = _service.Parse("[url=https://example.com]link[/url]");

        result.Should().Contain("href=\"https://example.com\"");
        result.Should().Contain("bbcode-link");
        result.Should().Contain(">link</a>");
    }

    [Fact]
    public void Parse_UrlWithoutAttribute()
    {
        var result = _service.Parse("[url]https://example.com[/url]");

        result.Should().Contain("href=\"https://example.com\"");
    }

    [Fact]
    public void Parse_ForumContext_AutoLinksBareUrls()
    {
        var result = _service.Parse("visit https://example.com/test please", BBCodeContext.Forum);

        result.Should().Contain("href=\"https://example.com/test\"");
        result.Should().Contain(">https://example.com/test</a>");
    }

    [Fact]
    public void Parse_CommentContext_DoesNotAutoLinkBareUrls()
    {
        var result = _service.Parse("visit https://example.com/test please", BBCodeContext.Comment);

        result.Should().NotContain("href=\"https://example.com/test\"");
        result.Should().Contain("https://example.com/test");
    }

    [Fact]
    public void Parse_ForumContext_ExcludesTrailingPunctuationFromAutoLinkedUrls()
    {
        var result = _service.Parse("see https://example.com/test).", BBCodeContext.Forum);

        result.Should().Contain("href=\"https://example.com/test\"");
        result.Should().Contain("</a>).");
    }

    [Fact]
    public void Parse_ForumContext_DoesNotAutoLinkInsideCode()
    {
        var result = _service.Parse("[code]https://example.com/test[/code]", BBCodeContext.Forum);

        result.Should().Contain("https://example.com/test");
        result.Should().NotContain("href=\"https://example.com/test\"");
    }

    [Fact]
    public void Parse_UrlWithInvalidProtocol_NotLinked()
    {
        var result = _service.Parse("[url=javascript:alert(1)]click[/url]");

        result.Should().NotContain("href=\"javascript:");
    }

    [Fact]
    public void Parse_QuoteWithInjectedPostId_DoesNotRenderJumpLinkOrInjectedHtml()
    {
        var result = _service.Parse("[quote postId=\"style=\"color:red\"><style>div{color:red;}</style>\" author=test]a[/quote]");

        result.Should().Contain("bbcode-quote");
        result.Should().Contain("bbcode-quote-author\">test</span>");
        result.Should().NotContain("bbcode-quote-link");
        result.Should().NotContain("<style>");
        result.Should().NotContain("style=\"color:red\"");
    }

    [Fact]
    public void Parse_QuoteWithValidPostId_RendersJumpLink()
    {
        var result = _service.Parse("[quote postId=123 author=test]a[/quote]");

        result.Should().Contain("href=\"#p123\"");
        result.Should().Contain("bbcode-quote-link");
    }

    [Fact]
    public void Parse_ThumbTag()
    {
        var result = _service.Parse("[thumb]123[/thumb]");

        result.Should().Contain("href=\"/post/view/123\"");
        result.Should().Contain("src=\"/api/booru/posts/123/thumbnail\"");
    }

    [Fact]
    public void Parse_ThumbTag_InvalidId()
    {
        var result = _service.Parse("[thumb]invalid[/thumb]");

        result.Should().Contain("[thumb]invalid[/thumb]");
        result.Should().NotContain("<a href");
    }

    [Fact]
    public void Parse_NestedTags()
    {
        var result = _service.Parse("[b][i]bold italic[/i][/b]");

        result.Should().Be("<strong><em>bold italic</em></strong>");
    }

    [Fact]
    public void Parse_ComplexDocument()
    {
        var input = """
            Hello [b]world[/b]!
            >greentext
            ==important==
            [url=https://example.com]link[/url]
            """;

        var result = _service.Parse(input);

        result.Should().Contain("<strong>world</strong>");
        result.Should().Contain("bbcode-greentext");
        result.Should().Contain("bbcode-redtext");
        result.Should().Contain("href=\"https://example.com\"");
    }

    [Fact]
    public void ParseToAst_ForumContext_ConvertsBareUrlToUrlNode()
    {
        var nodes = _service.ParseToAst("https://example.com/test", BBCodeContext.Forum);

        nodes.Should().ContainSingle();
        var urlNode = nodes[0].Should().BeOfType<UrlNodeDto>().Subject;
        urlNode.Href.Should().Be("https://example.com/test");
    }

    [Fact]
    public void Parse_TruncatesLongInput()
    {
        var longInput = new string('a', 15000);

        var result = _service.Parse(longInput);

        result.Length.Should().BeLessOrEqualTo(10000);
    }

    [Fact]
    public void Validate_EmptyString_ReturnsTrue()
    {
        var isValid = _service.Validate("", out var errors);

        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_BalancedTags_ReturnsTrue()
    {
        var isValid = _service.Validate("[b]bold[/b]", out var errors);

        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_UnclosedTag_ReturnsFalse()
    {
        var isValid = _service.Validate("[b]unclosed", out var errors);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("Unclosed tag"));
    }

    [Fact]
    public void Validate_UnexpectedCloseTag_ReturnsFalse()
    {
        var isValid = _service.Validate("[/b]", out var errors);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("Unexpected closing tag"));
    }

    [Fact]
    public void Validate_MismatchedTags_ReturnsFalse()
    {
        var isValid = _service.Validate("[b][i]text[/b][/i]", out var errors);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_TooLongInput_ReturnsFalse()
    {
        var longInput = new string('a', 15000);

        var isValid = _service.Validate(longInput, out var errors);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("maximum length"));
    }

    [Fact]
    public void Parse_CaseInsensitiveTags()
    {
        var result = _service.Parse("[B]bold[/B]");

        result.Should().Be("<strong>bold</strong>");
    }

    [Fact]
    public void Parse_HeadingTags()
    {
        for (int i = 1; i <= 4; i++)
        {
            var result = _service.Parse($"[h{i}]heading[/h{i}]");
            result.Should().Contain($"<h{i}");
            result.Should().Contain($"</h{i}>");
        }
    }

    [Fact]
    public void Parse_SubSupTags()
    {
        var subResult = _service.Parse("H[sub]2[/sub]O");
        var supResult = _service.Parse("x[sup]2[/sup]");

        subResult.Should().Contain("<sub>2</sub>");
        supResult.Should().Contain("<sup>2</sup>");
    }

    [Fact]
    public void Parse_UnorderedList_RendersUlLi()
    {
        var result = _service.Parse("[list][*]one[*]two[/list]");

        result.Should().Contain("<ul>");
        result.Should().Contain("<li>one</li>");
        result.Should().Contain("<li>two</li>");
        result.Should().Contain("</ul>");
    }

    [Fact]
    public void Parse_OrderedList_RendersOlLi()
    {
        var result = _service.Parse("[list=1][*]first[*]second[/list]");

        result.Should().Contain("<ol>");
        result.Should().Contain("<li>first</li>");
        result.Should().Contain("<li>second</li>");
        result.Should().Contain("</ol>");
    }

    [Fact]
    public void Parse_ListWithNewlines_RendersCorrectly()
    {
        var result = _service.Parse("[list]\n[*]alpha\n[*]beta\n[/list]");

        result.Should().Contain("<ul>");
        result.Should().Contain("<li>alpha");
        result.Should().Contain("<li>beta");
        result.Should().Contain("</ul>");
    }

    [Fact]
    public void Parse_ListItemsWithFormatting_PreservesInnerTags()
    {
        var result = _service.Parse("[list][*][b]bold item[/b][*]plain item[/list]");

        result.Should().Contain("<li><strong>bold item</strong></li>");
        result.Should().Contain("<li>plain item</li>");
    }

    [Fact]
    public void Parse_EmptyList_DoesNotCrash()
    {
        var result = _service.Parse("[list][/list]");

        result.Should().Contain("<ul>");
        result.Should().Contain("</ul>");
    }

    [Fact]
    public void Parse_SingleListItem_Works()
    {
        var result = _service.Parse("[list][*]only one[/list]");

        result.Should().Contain("<ul>");
        result.Should().Contain("<li>only one</li>");
        result.Should().Contain("</ul>");
    }
}

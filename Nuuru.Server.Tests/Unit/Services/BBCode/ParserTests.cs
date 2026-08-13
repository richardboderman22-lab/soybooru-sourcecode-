using FluentAssertions;
using Nuuru.Server.Services.BBCode;

namespace Nuuru.Server.Tests.Unit.Services.BBCode;

public class ParserTests
{
    private List<BbNode> Parse(string input)
    {
        var tokenizer = new BbTokenizer(input);
        var tokens = tokenizer.Tokenize();
        var parser = new BbParser(tokens);
        return parser.Parse();
    }

    [Fact]
    public void Parse_PlainText_ReturnsTextNode()
    {
        var nodes = Parse("Hello world");

        nodes.Should().HaveCount(1);
        nodes[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be("Hello world");
    }

    [Fact]
    public void Parse_Newline_ReturnsNewlineNode()
    {
        var nodes = Parse("Line1\nLine2");

        nodes.Should().HaveCount(3);
        nodes[1].Should().BeOfType<NewlineNode>();
    }

    [Fact]
    public void Parse_BoldTag_ReturnsElementNode()
    {
        var nodes = Parse("[b]bold[/b]");

        nodes.Should().HaveCount(1);
        var element = nodes[0].Should().BeOfType<ElementNode>().Subject;
        element.Tag.Should().Be("b");
        element.Children.Should().HaveCount(1);
        element.Children[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be("bold");
    }

    [Fact]
    public void Parse_NestedTags_ReturnsNestedElements()
    {
        var nodes = Parse("[b][i]bold italic[/i][/b]");

        nodes.Should().HaveCount(1);
        var bold = nodes[0].Should().BeOfType<ElementNode>().Subject;
        bold.Tag.Should().Be("b");
        bold.Children.Should().HaveCount(1);

        var italic = bold.Children[0].Should().BeOfType<ElementNode>().Subject;
        italic.Tag.Should().Be("i");
        italic.Children.Should().HaveCount(1);
        italic.Children[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be("bold italic");
    }

    [Fact]
    public void Parse_UnclosedTag_ParsesRemainderAsChildren()
    {
        var nodes = Parse("[b]unclosed");

        nodes.Should().HaveCount(1);
        var element = nodes[0].Should().BeOfType<ElementNode>().Subject;
        element.Tag.Should().Be("b");
        element.Children.Should().HaveCount(1);
        element.Children[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be("unclosed");
    }

    [Fact]
    public void Parse_OrphanCloseTag_TreatedAsText()
    {
        var nodes = Parse("[/b]");

        nodes.Should().HaveCount(1);
        nodes[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be("[/b]");
    }

    [Fact]
    public void Parse_UnknownTag_TreatedAsText()
    {
        var nodes = Parse("[unknown]text[/unknown]");

        // Unknown tags are rendered as text nodes (open tag, content, close tag)
        nodes.Should().HaveCount(3);
        nodes[0].Should().BeOfType<TextNode>().Which.Content.Should().Be("[unknown]");
        nodes[1].Should().BeOfType<TextNode>().Which.Content.Should().Be("text");
        nodes[2].Should().BeOfType<TextNode>().Which.Content.Should().Be("[/unknown]");
    }

    [Fact]
    public void Parse_Greentext_ReturnsElementNode()
    {
        var nodes = Parse(">greentext");

        nodes.Should().HaveCount(1);
        var el = nodes[0].Should().BeOfType<ElementNode>().Subject;
        el.Tag.Should().Be("greentext");
        el.Attribute.Should().Be("line");
        el.Children.Should().HaveCount(1);
        el.Children[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be("greentext");
    }

    [Fact]
    public void Parse_Redtext_ReturnsElementNode()
    {
        var nodes = Parse("==redtext==");

        nodes.Should().HaveCount(1);
        var el = nodes[0].Should().BeOfType<ElementNode>().Subject;
        el.Tag.Should().Be("redtext");
        el.Children.Should().HaveCount(1);
        el.Children[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be("redtext");
    }

    [Fact]
    public void Parse_ThumbTag_ReturnsThumbNode()
    {
        var nodes = Parse("[thumb]123[/thumb]");

        nodes.Should().HaveCount(1);
        var thumb = nodes[0].Should().BeOfType<ThumbNode>().Subject;
        thumb.PostId.Should().Be(123);
    }

    [Fact]
    public void Parse_ThumbTag_InvalidId_ReturnsTextNode()
    {
        var nodes = Parse("[thumb]invalid[/thumb]");

        nodes.Should().HaveCount(1);
        nodes[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Contain("[thumb]");
    }

    [Fact]
    public void Parse_UrlWithAttribute_ReturnsUrlNode()
    {
        var nodes = Parse("[url=https://example.com]link text[/url]");

        nodes.Should().HaveCount(1);
        var url = nodes[0].Should().BeOfType<UrlNode>().Subject;
        url.Href.Should().Be("https://example.com");
        url.Children.Should().HaveCount(1);
        url.Children[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be("link text");
    }

    [Fact]
    public void Parse_UrlWithoutAttribute_ExtractsUrlFromContent()
    {
        var nodes = Parse("[url]https://example.com[/url]");

        nodes.Should().HaveCount(1);
        var url = nodes[0].Should().BeOfType<UrlNode>().Subject;
        url.Href.Should().Be("https://example.com");
    }

    [Fact]
    public void Parse_UrlWithInvalidHref_ReturnsElementNode()
    {
        var nodes = Parse("[url=javascript:alert(1)]click me[/url]");

        nodes.Should().HaveCount(1);
        // Invalid URL should be rendered as plain element, not UrlNode
        nodes[0].Should().BeOfType<ElementNode>();
    }

    [Fact]
    public void Parse_SpoilerTag_ReturnsElementNode()
    {
        var nodes = Parse("[spoiler]hidden[/spoiler]");

        nodes.Should().HaveCount(1);
        var element = nodes[0].Should().BeOfType<ElementNode>().Subject;
        element.Tag.Should().Be("spoiler");
    }

    [Fact]
    public void Parse_CodeTag_ReturnsElementNode()
    {
        var nodes = Parse("[code]console.log('hello')[/code]");

        nodes.Should().HaveCount(1);
        var element = nodes[0].Should().BeOfType<ElementNode>().Subject;
        element.Tag.Should().Be("code");
    }

    [Fact]
    public void Parse_AllDelimitedTypes()
    {
        var cases = new (string input, string expectedTag)[]
        {
            ("==red==", "redtext"),
            ("--blue--", "bluetext"),
            ("%%glow%%", "glowtext"),
            ("!!redglow!!", "redglow"),
            ("::yellow::", "yellowglow"),
            ("~-~rainbow~-~", "rainbow"),
            (">green", "greentext"),
            ("<orange", "orangetext"),
        };

        foreach (var (input, expectedTag) in cases)
        {
            var nodes = Parse(input);
            nodes.Should().HaveCount(1, $"Input: {input}");
            var el = nodes[0].Should().BeOfType<ElementNode>().Subject;
            el.Tag.Should().Be(expectedTag, $"Input: {input}");
        }
    }

    [Fact]
    public void Parse_DelimitedTypes_SupportInlineChildren()
    {
        var cases = new (string input, string expectedTag)[]
        {
            ("==[b]bold[/b]==", "redtext"),
            ("--[i]italic[/i]--", "bluetext"),
            ("%%[b]bold[/b]%%", "glowtext"),
        };

        foreach (var (input, expectedTag) in cases)
        {
            var nodes = Parse(input);
            nodes.Should().HaveCount(1, $"Input: {input}");
            var el = nodes[0].Should().BeOfType<ElementNode>().Subject;
            el.Tag.Should().Be(expectedTag, $"Input: {input}");
            el.Children.Should().HaveCount(1, $"Input: {input}");
            el.Children[0].Should().BeOfType<ElementNode>($"Input: {input}");
        }
    }

    [Fact]
    public void Parse_GreentextAndOrangetext_ProduceLineAttribute()
    {
        // Greentext/orangetext produce ElementNode with attribute "line"
        var containerCases = new (string input, string tag)[]
        {
            (">green", "greentext"),
            ("<orange", "orangetext"),
        };

        foreach (var (input, tag) in containerCases)
        {
            var nodes = Parse(input);
            nodes.Should().HaveCount(1, $"Input: {input}");
            var el = nodes[0].Should().BeOfType<ElementNode>().Subject;
            el.Tag.Should().Be(tag, $"Input: {input}");
            el.Attribute.Should().Be("line", $"Input: {input}");
        }
    }

    [Fact]
    public void Parse_ComplexNesting()
    {
        var nodes = Parse("[b]bold [i]and italic[/i] text[/b]");

        nodes.Should().HaveCount(1);
        var bold = nodes[0].Should().BeOfType<ElementNode>().Subject;
        bold.Tag.Should().Be("b");
        bold.Children.Should().HaveCount(3);

        bold.Children[0].Should().BeOfType<TextNode>().Which.Content.Should().Be("bold ");
        bold.Children[1].Should().BeOfType<ElementNode>().Which.Tag.Should().Be("i");
        bold.Children[2].Should().BeOfType<TextNode>().Which.Content.Should().Be(" text");
    }

    [Fact]
    public void Parse_Mention_WithPostIdAndCommentId()
    {
        var guid = Guid.NewGuid();
        var nodes = Parse($"[mention userguid={guid} postid=5 commentid=10]@User[/mention]");

        nodes.Should().HaveCount(1);
        var mention = nodes[0].Should().BeOfType<MentionNode>().Subject;
        mention.UserId.Should().Be(guid);
        mention.UserName.Should().Be("User");
        mention.PostId.Should().Be(5);
        mention.CommentId.Should().Be(10);
    }

    [Fact]
    public void Parse_Mention_WithPostIdOnly()
    {
        var guid = Guid.NewGuid();
        var nodes = Parse($"[mention userguid={guid} postid=42]@User[/mention]");

        nodes.Should().HaveCount(1);
        var mention = nodes[0].Should().BeOfType<MentionNode>().Subject;
        mention.PostId.Should().Be(42);
        mention.CommentId.Should().BeNull();
    }

    [Fact]
    public void Parse_Mention_WithoutPostId_DefaultsToNull()
    {
        var guid = Guid.NewGuid();
        var nodes = Parse($"[mention userguid={guid}]@User[/mention]");

        nodes.Should().HaveCount(1);
        var mention = nodes[0].Should().BeOfType<MentionNode>().Subject;
        mention.PostId.Should().BeNull();
        mention.CommentId.Should().BeNull();
    }

}

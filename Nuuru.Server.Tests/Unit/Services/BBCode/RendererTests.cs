using FluentAssertions;
using Nuuru.Server.Services.BBCode;

namespace Nuuru.Server.Tests.Unit.Services.BBCode;

public class RendererTests
{
    private readonly BbRenderer _renderer = new();

    [Fact]
    public void Render_TextNode_HtmlEncodesContent()
    {
        var nodes = new List<BbNode> { new TextNode("<script>alert('xss')</script>") };

        var html = _renderer.Render(nodes);

        html.Should().Be("&lt;script&gt;alert(&#x27;xss&#x27;)&lt;/script&gt;");
    }

    [Fact]
    public void Render_NewlineNode_ReturnsBr()
    {
        var nodes = new List<BbNode> { new NewlineNode() };

        var html = _renderer.Render(nodes);

        html.Should().Be("<br>");
    }

    [Fact]
    public void Render_BoldElement_ReturnsStrong()
    {
        var nodes = new List<BbNode>
        {
            new ElementNode("b", null, new List<BbNode> { new TextNode("bold") })
        };

        var html = _renderer.Render(nodes);

        html.Should().Be("<strong>bold</strong>");
    }

    [Fact]
    public void Render_ItalicElement_ReturnsEm()
    {
        var nodes = new List<BbNode>
        {
            new ElementNode("i", null, new List<BbNode> { new TextNode("italic") })
        };

        var html = _renderer.Render(nodes);

        html.Should().Be("<em>italic</em>");
    }

    [Fact]
    public void Render_StrikeElement_ReturnsDel()
    {
        var nodes = new List<BbNode>
        {
            new ElementNode("s", null, new List<BbNode> { new TextNode("strike") })
        };

        var html = _renderer.Render(nodes);

        html.Should().Be("<del>strike</del>");
    }

    [Fact]
    public void Render_UnderlineElement_ReturnsSpanWithClass()
    {
        var nodes = new List<BbNode>
        {
            new ElementNode("u", null, new List<BbNode> { new TextNode("underline") })
        };

        var html = _renderer.Render(nodes);

        html.Should().Be("<span class=\"bbcode-underline\">underline</span>");
    }

    [Fact]
    public void Render_CodeElement_ReturnsPreCode()
    {
        var nodes = new List<BbNode>
        {
            new ElementNode("code", null, new List<BbNode> { new TextNode("code") })
        };

        var html = _renderer.Render(nodes);

        html.Should().Be("<pre class=\"bbcode-code\"><code>code</code></pre>");
    }

    [Fact]
    public void Render_SpoilerElement_ReturnsSpanWithClass()
    {
        var nodes = new List<BbNode>
        {
            new ElementNode("spoiler", null, new List<BbNode> { new TextNode("hidden") })
        };

        var html = _renderer.Render(nodes);

        html.Should().Be("<span class=\"bbcode-spoiler\">hidden</span>");
    }

    [Fact]
    public void Render_HeadingElements()
    {
        for (int i = 1; i <= 4; i++)
        {
            var nodes = new List<BbNode>
            {
                new ElementNode($"h{i}", null, new List<BbNode> { new TextNode("heading") })
            };

            var html = _renderer.Render(nodes);

            html.Should().Be($"<h{i} class=\"bbcode-heading\">heading</h{i}>");
        }
    }

    [Fact]
    public void Render_SubSup()
    {
        var subNodes = new List<BbNode>
        {
            new ElementNode("sub", null, new List<BbNode> { new TextNode("2") })
        };
        var supNodes = new List<BbNode>
        {
            new ElementNode("sup", null, new List<BbNode> { new TextNode("2") })
        };

        _renderer.Render(subNodes).Should().Be("<sub>2</sub>");
        _renderer.Render(supNodes).Should().Be("<sup>2</sup>");
    }

    [Fact]
    public void Render_GreentextLine()
    {
        var nodes = new List<BbNode>
        {
            new ElementNode("greentext", "line", new List<BbNode> { new TextNode("greentext") })
        };

        var html = _renderer.Render(nodes);

        html.Should().Be("<span class=\"bbcode-greentext\">&gt;greentext</span>");
    }

    [Fact]
    public void Render_AllDelimitedTypes()
    {
        var testCases = new (string tag, string expectedClass)[]
        {
            ("redtext", "bbcode-redtext"),
            ("bluetext", "bbcode-bluetext"),
            ("glowtext", "bbcode-glow"),
            ("redglow", "bbcode-glow-red"),
            ("yellowglow", "bbcode-glow-yellow"),
            ("rainbow", "bbcode-rainbow"),
        };

        foreach (var (tag, expectedClass) in testCases)
        {
            var nodes = new List<BbNode> { new ElementNode(tag, null, new List<BbNode> { new TextNode("content") }) };
            var html = _renderer.Render(nodes);
            html.Should().Contain($"class=\"{expectedClass}\"", $"Tag: {tag}");
        }
    }

    [Fact]
    public void Render_GreentextLine_HtmlEncoded()
    {
        var nodes = new List<BbNode>
        {
            new ElementNode("greentext", "line", new List<BbNode> { new TextNode("<script>") })
        };

        var html = _renderer.Render(nodes);

        html.Should().Contain("&lt;script&gt;");
        html.Should().NotContain("<script>");
    }

    [Fact]
    public void Render_ThumbNode()
    {
        var nodes = new List<BbNode> { new ThumbNode(123) };

        var html = _renderer.Render(nodes);

        html.Should().Contain("href=\"/post/view/123\"");
        html.Should().Contain("src=\"/api/booru/posts/123/thumbnail\"");
        html.Should().Contain("class=\"bbcode-thumb\"");
    }

    [Fact]
    public void Render_UrlNode()
    {
        var nodes = new List<BbNode>
        {
            new UrlNode("https://example.com", new List<BbNode> { new TextNode("link") })
        };

        var html = _renderer.Render(nodes);

        html.Should().Contain("href=\"https://example.com\"");
        html.Should().Contain("class=\"bbcode-link\"");
        html.Should().Contain("rel=\"nofollow noopener\"");
        html.Should().Contain("target=\"_blank\"");
        html.Should().Contain(">link</a>");
    }

    [Fact]
    public void Render_UrlNode_HtmlEncodesHref()
    {
        var nodes = new List<BbNode>
        {
            new UrlNode("https://example.com/path?a=1&b=2", new List<BbNode> { new TextNode("link") })
        };

        var html = _renderer.Render(nodes);

        html.Should().Contain("&amp;");
    }

    [Fact]
    public void Render_NestedElements()
    {
        var nodes = new List<BbNode>
        {
            new ElementNode("b", null, new List<BbNode>
            {
                new ElementNode("i", null, new List<BbNode>
                {
                    new TextNode("bold italic")
                })
            })
        };

        var html = _renderer.Render(nodes);

        html.Should().Be("<strong><em>bold italic</em></strong>");
    }

    [Fact]
    public void Render_ComplexDocument()
    {
        var nodes = new List<BbNode>
        {
            new TextNode("Hello "),
            new ElementNode("b", null, new List<BbNode> { new TextNode("world") }),
            new TextNode("!"),
            new NewlineNode(),
            new ElementNode("greentext", "line", new List<BbNode> { new TextNode("greentext") })
        };

        var html = _renderer.Render(nodes);

        html.Should().Be("Hello <strong>world</strong>!<br><span class=\"bbcode-greentext\">&gt;greentext</span>");
    }

    [Fact]
    public void Render_EmptyNodeList_ReturnsEmptyString()
    {
        var html = _renderer.Render(new List<BbNode>());

        html.Should().BeEmpty();
    }

    [Fact]
    public void Render_MentionNode_DefaultLinksToUserProfile()
    {
        var nodes = new List<BbNode>
        {
            new MentionNode(Guid.NewGuid(), "TestUser")
        };

        var html = _renderer.Render(nodes);

        html.Should().Be("<a href=\"/user/TestUser\" class=\"bbcode-mention\">@TestUser</a>");
    }

    [Fact]
    public void Render_MentionNode_WithPostIdAndCommentId_LinksToComment()
    {
        var nodes = new List<BbNode>
        {
            new MentionNode(Guid.NewGuid(), "TestUser", postId: 5, commentId: 10)
        };

        var html = _renderer.Render(nodes);

        html.Should().Be("<a href=\"/post/view/5#c10\" class=\"bbcode-mention\">@TestUser</a>");
    }

    [Fact]
    public void Render_MentionNode_WithPostIdOnly_LinksToPost()
    {
        var nodes = new List<BbNode>
        {
            new MentionNode(Guid.NewGuid(), "TestUser", postId: 42)
        };

        var html = _renderer.Render(nodes);

        html.Should().Be("<a href=\"/post/view/42\" class=\"bbcode-mention\">@TestUser</a>");
    }

    [Fact]
    public void Render_MentionNode_HtmlEncodesUserName()
    {
        var nodes = new List<BbNode>
        {
            new MentionNode(Guid.NewGuid(), "<script>xss</script>", postId: 1, commentId: 2)
        };

        var html = _renderer.Render(nodes);

        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
    }
}

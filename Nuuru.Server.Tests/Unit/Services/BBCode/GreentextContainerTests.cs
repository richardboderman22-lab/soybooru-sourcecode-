using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Nuuru.Server.Services.BBCode;

namespace Nuuru.Server.Tests.Unit.Services.BBCode;

/// <summary>
/// Tests that greentext/orangetext act as container nodes, allowing
/// inner BBCode to be parsed while forcing green/orange color via CSS.
/// </summary>
public class GreentextContainerTests
{
    private readonly BBCodeService _service = new();

    // ── Tokenizer ──────────────────────────────────────────────────

    [Fact]
    public void Tokenizer_Greentext_EmitsMarkerThenContent()
    {
        var tokenizer = new BbTokenizer(">hello");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(2);
        tokens[0].Should().BeOfType<GreentextToken>();
        tokens[1].Should().BeOfType<TextToken>()
            .Which.Content.Should().Be("hello");
    }

    [Fact]
    public void Tokenizer_Orangetext_EmitsMarkerThenContent()
    {
        var tokenizer = new BbTokenizer("<hello");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(2);
        tokens[0].Should().BeOfType<OrangetextToken>();
        tokens[1].Should().BeOfType<TextToken>()
            .Which.Content.Should().Be("hello");
    }

    [Fact]
    public void Tokenizer_Greentext_WithBBCode_EmitsMarkerThenTags()
    {
        var tokenizer = new BbTokenizer(">[b]bold[/b]");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(4);
        tokens[0].Should().BeOfType<GreentextToken>();
        tokens[1].Should().BeOfType<OpenTagToken>();
        tokens[2].Should().BeOfType<TextToken>();
        tokens[3].Should().BeOfType<CloseTagToken>();
    }

    [Fact]
    public void Tokenizer_EmptyGreentext_EmitsMarkerOnly()
    {
        // > at end of input
        var tokenizer = new BbTokenizer(">");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        tokens[0].Should().BeOfType<GreentextToken>();
    }

    [Fact]
    public void Tokenizer_GreentextBeforeNewline_EmitsMarkerThenNewline()
    {
        var tokenizer = new BbTokenizer(">\n");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(2);
        tokens[0].Should().BeOfType<GreentextToken>();
        tokens[1].Should().BeOfType<NewlineToken>();
    }

    // ── Parser ─────────────────────────────────────────────────────

    [Fact]
    public void Parser_Greentext_ProducesElementNode()
    {
        var tokens = new BbTokenizer(">hello").Tokenize();
        var ast = new BbParser(tokens).Parse();

        ast.Should().HaveCount(1);
        var el = ast[0].Should().BeOfType<ElementNode>().Subject;
        el.Tag.Should().Be("greentext");
        el.Children.Should().HaveCount(1);
        el.Children[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be("hello");
    }

    [Fact]
    public void Parser_Orangetext_ProducesElementNode()
    {
        var tokens = new BbTokenizer("<hello").Tokenize();
        var ast = new BbParser(tokens).Parse();

        ast.Should().HaveCount(1);
        var el = ast[0].Should().BeOfType<ElementNode>().Subject;
        el.Tag.Should().Be("orangetext");
        el.Children.Should().HaveCount(1);
        el.Children[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be("hello");
    }

    [Fact]
    public void Parser_EmptyGreentext_ProducesTextNode()
    {
        // > followed by newline — no children, fallback to literal >
        var tokens = new BbTokenizer(">\ntext").Tokenize();
        var ast = new BbParser(tokens).Parse();

        ast[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be(">");
    }

    [Fact]
    public void Parser_EmptyOrangetext_ProducesTextNode()
    {
        var tokens = new BbTokenizer("<\ntext").Tokenize();
        var ast = new BbParser(tokens).Parse();

        ast[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be("<");
    }

    [Fact]
    public void Parser_Greentext_WithBBCode_ParsesInnerTags()
    {
        var tokens = new BbTokenizer(">[b]bold[/b]").Tokenize();
        var ast = new BbParser(tokens).Parse();

        var greentext = ast[0].Should().BeOfType<ElementNode>().Subject;
        greentext.Tag.Should().Be("greentext");
        greentext.Children.Should().HaveCount(1);

        var bold = greentext.Children[0].Should().BeOfType<ElementNode>().Subject;
        bold.Tag.Should().Be("b");
        bold.Children[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be("bold");
    }

    [Fact]
    public void Parser_Greentext_StopsAtNewline()
    {
        var tokens = new BbTokenizer(">green\nnormal").Tokenize();
        var ast = new BbParser(tokens).Parse();

        ast.Should().HaveCount(3); // greentext, newline, text
        ast[0].Should().BeOfType<ElementNode>()
            .Which.Tag.Should().Be("greentext");
        ast[1].Should().BeOfType<NewlineNode>();
        ast[2].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be("normal");
    }

    [Fact]
    public void Parser_Greentext_StopsAtCloseTag_DoesNotConsumeIt()
    {
        // Inside a quote: >greentext[/quote] — must not eat [/quote]
        var bbcode = "[quote=User]>greentext[/quote]";
        var tokens = new BbTokenizer(bbcode).Tokenize();
        var ast = new BbParser(tokens).Parse();

        var quote = ast.OfType<QuoteNode>().FirstOrDefault();
        quote.Should().NotBeNull("the [/quote] should not be consumed by greentext");
    }

    // ── Renderer (full pipeline) ───────────────────────────────────

    [Fact]
    public void Render_Greentext_ContainsClassAndPrefix()
    {
        var result = _service.Parse(">implying");

        result.Should().Contain("bbcode-greentext");
        result.Should().Contain("&gt;implying");
    }

    [Fact]
    public void Render_Orangetext_ContainsClassAndPrefix()
    {
        var result = _service.Parse("<orange");

        result.Should().Contain("bbcode-orangetext");
        result.Should().Contain("&lt;orange");
    }

    [Fact]
    public void Render_Greentext_WithBold_RendersBoldInsideGreenSpan()
    {
        var result = _service.Parse(">[b]bold text[/b]");

        result.Should().Contain("bbcode-greentext");
        result.Should().Contain("<strong>bold text</strong>");
        result.Should().Contain("&gt;");
    }

    [Fact]
    public void Render_Greentext_WithItalic_RendersItalicInsideGreenSpan()
    {
        var result = _service.Parse(">[i]italic[/i]");

        result.Should().Contain("bbcode-greentext");
        result.Should().Contain("<em>italic</em>");
    }

    [Fact]
    public void Render_Greentext_WithColor_RendersColorInsideGreenSpan()
    {
        var result = _service.Parse(">[color=red]colored[/color]");

        result.Should().Contain("bbcode-greentext");
        result.Should().Contain("style=\"color:red\"");
        result.Should().Contain("colored");
    }

    [Fact]
    public void Render_Greentext_WithFont_RendersFontInsideGreenSpan()
    {
        var result = _service.Parse(">[font=Arial]styled[/font]");

        result.Should().Contain("bbcode-greentext");
        result.Should().Contain("font-family:'Arial'");
    }

    [Fact]
    public void Render_Greentext_WithNestedFormatting()
    {
        var result = _service.Parse(">[b][i]bold italic[/i][/b]");

        result.Should().Contain("bbcode-greentext");
        result.Should().Contain("<strong><em>bold italic</em></strong>");
    }

    [Fact]
    public void Render_Orangetext_WithBold_RendersBoldInsideOrangeSpan()
    {
        var result = _service.Parse("<[b]bold[/b]");

        result.Should().Contain("bbcode-orangetext");
        result.Should().Contain("<strong>bold</strong>");
        result.Should().Contain("&lt;");
    }

    [Fact]
    public void Render_Greentext_WithSpoiler()
    {
        var result = _service.Parse(">[spoiler]hidden[/spoiler]");

        result.Should().Contain("bbcode-greentext");
        result.Should().Contain("bbcode-spoiler");
    }

    [Fact]
    public void Render_Greentext_MixedTextAndBBCode()
    {
        var result = _service.Parse(">some [b]bold[/b] text");

        result.Should().Contain("bbcode-greentext");
        result.Should().Contain("&gt;some ");
        result.Should().Contain("<strong>bold</strong>");
        result.Should().Contain(" text");
    }

    [Fact]
    public void Render_EmptyGreentext_RendersLiteralAngleBracket()
    {
        // > followed by newline
        var result = _service.Parse(">\ntext");

        result.Should().Contain("&gt;");
        result.Should().Contain("text");
        result.Should().NotContain("bbcode-greentext");
    }

    [Fact]
    public void Render_EmptyOrangetext_RendersLiteralAngleBracket()
    {
        var result = _service.Parse("<\ntext");

        result.Should().Contain("&lt;");
        result.Should().Contain("text");
        result.Should().NotContain("bbcode-orangetext");
    }

    [Fact]
    public void Render_MultipleGreentextLines()
    {
        var result = _service.Parse(">line one\n>line two");

        // Should have two separate greentext elements
        var count = result.Split("bbcode-greentext").Length - 1;
        count.Should().Be(2);
    }

    // ── Text extraction (for quote verification) ───────────────────

    [Fact]
    public void ExtractPlainText_Greentext_ExtractsContentWithoutPrefix()
    {
        var text = _service.ExtractPlainText(">hello world");

        text.Should().Be("hello world");
    }

    [Fact]
    public void ExtractPlainText_Orangetext_ExtractsContentWithoutPrefix()
    {
        var text = _service.ExtractPlainText("<orange text");

        text.Should().Be("orange text");
    }

    [Fact]
    public void ExtractPlainText_GreentextWithBBCode_ExtractsInnerText()
    {
        var text = _service.ExtractPlainText(">[b]bold[/b] text");

        text.Should().Be("bold text");
    }

    [Fact]
    public void ExtractPlainText_GreentextWithNestedFormatting_ExtractsAllText()
    {
        var text = _service.ExtractPlainText(">[b][i]bold italic[/i][/b]");

        text.Should().Be("bold italic");
    }

    // ── Quote verification round-trips ─────────────────────────────

    private readonly QuoteChecksumService _checksumService;

    public GreentextContainerTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BBCode:QuoteSigningKey"] = "test-signing-key-for-unit-tests"
            })
            .Build();
        _checksumService = new QuoteChecksumService(config);
    }

    private bool VerifyRoundTrip(string contentRaw)
    {
        var plainText = _service.ExtractPlainText(contentRaw);
        var hash = _checksumService.GenerateHash("comment", "1", plainText);
        var quoteBBCode = $"[quote commentId=1 author=TestUser hash={hash}]{contentRaw}[/quote]";

        var tokenizer = new BbTokenizer(quoteBBCode);
        var tokens = tokenizer.Tokenize();
        var parser = new BbParser(tokens);
        var ast = parser.Parse();

        var quoteNode = ast.OfType<QuoteNode>().FirstOrDefault();
        if (quoteNode == null) return false;

        var extractedText = ExtractTextContent(quoteNode.Children);
        return _checksumService.VerifyHash("comment", "1", extractedText, quoteNode.ProvidedHash!);
    }

    private static string ExtractTextContent(List<BbNode> nodes)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode t: sb.Append(t.Content); break;
                case NewlineNode: sb.Append('\n'); break;
                case ElementNode e: sb.Append(ExtractTextContent(e.Children)); break;
                case QuoteNode q: sb.Append(ExtractTextContent(q.Children)); break;
                case UrlNode u: sb.Append(ExtractTextContent(u.Children)); break;
                case MentionNode m: sb.Append('@'); sb.Append(m.UserName); break;
            }
        }
        return sb.ToString();
    }

    [Fact]
    public void QuoteVerify_GreentextWithBold_Verifies()
    {
        VerifyRoundTrip(">[b]bold[/b]").Should().BeTrue();
    }

    [Fact]
    public void QuoteVerify_GreentextWithColor_Verifies()
    {
        VerifyRoundTrip(">[color=red]colored[/color]").Should().BeTrue();
    }

    [Fact]
    public void QuoteVerify_GreentextWithFont_Verifies()
    {
        VerifyRoundTrip(">[font=Arial]styled[/font]").Should().BeTrue();
    }

    [Fact]
    public void QuoteVerify_GreentextWithNestedFormatting_Verifies()
    {
        VerifyRoundTrip(">[b][i]bold italic[/i][/b]").Should().BeTrue();
    }

    [Fact]
    public void QuoteVerify_GreentextWithMixedTextAndBBCode_Verifies()
    {
        VerifyRoundTrip(">some [b]bold[/b] text").Should().BeTrue();
    }

    [Fact]
    public void QuoteVerify_OrangetextWithBold_Verifies()
    {
        VerifyRoundTrip("<[b]bold[/b]").Should().BeTrue();
    }

    [Fact]
    public void QuoteVerify_MultipleGreentextLinesWithBBCode_Verifies()
    {
        VerifyRoundTrip(">[b]first[/b]\n>[i]second[/i]").Should().BeTrue();
    }

    [Fact]
    public void QuoteVerify_GreentextWithSpoiler_Verifies()
    {
        VerifyRoundTrip(">[spoiler]hidden[/spoiler]").Should().BeTrue();
    }

    [Fact]
    public void QuoteVerify_ComplexGreentextContent_Verifies()
    {
        var content = """
            >[font=Arial][color=red]styled text[/color][/font]
            >normal greentext
            plain text
            """;
        VerifyRoundTrip(content).Should().BeTrue();
    }

    [Fact]
    public void QuoteVerify_GreentextWithUrl_Verifies()
    {
        VerifyRoundTrip(">[url=https://example.com]link[/url]").Should().BeTrue();
    }
}

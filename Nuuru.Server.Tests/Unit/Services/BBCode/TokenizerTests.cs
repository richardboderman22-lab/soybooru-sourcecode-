using FluentAssertions;
using Nuuru.Server.Services.BBCode;

namespace Nuuru.Server.Tests.Unit.Services.BBCode;

public class TokenizerTests
{
    [Fact]
    public void Tokenize_PlainText_ReturnsSingleTextToken()
    {
        var tokenizer = new BbTokenizer("Hello world");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        tokens[0].Should().BeOfType<TextToken>()
            .Which.Content.Should().Be("Hello world");
    }

    [Fact]
    public void Tokenize_Newline_ReturnsNewlineToken()
    {
        var tokenizer = new BbTokenizer("Line1\nLine2");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(3);
        tokens[0].Should().BeOfType<TextToken>().Which.Content.Should().Be("Line1");
        tokens[1].Should().BeOfType<NewlineToken>();
        tokens[2].Should().BeOfType<TextToken>().Which.Content.Should().Be("Line2");
    }

    [Fact]
    public void Tokenize_NormalizesWindowsNewlines()
    {
        var tokenizer = new BbTokenizer("Line1\r\nLine2");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(3);
        tokens[1].Should().BeOfType<NewlineToken>();
    }

    [Fact]
    public void Tokenize_OpenTag_ReturnsOpenTagToken()
    {
        var tokenizer = new BbTokenizer("[b]bold[/b]");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(3);
        tokens[0].Should().BeOfType<OpenTagToken>().Which.Name.Should().Be("b");
        tokens[1].Should().BeOfType<TextToken>().Which.Content.Should().Be("bold");
        tokens[2].Should().BeOfType<CloseTagToken>().Which.Name.Should().Be("b");
    }

    [Fact]
    public void Tokenize_TagWithAttribute_ParsesAttribute()
    {
        var tokenizer = new BbTokenizer("[url=https://example.com]link[/url]");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(3);
        var openTag = tokens[0].Should().BeOfType<OpenTagToken>().Subject;
        openTag.Name.Should().Be("url");
        openTag.Attribute.Should().Be("https://example.com");
    }

    [Fact]
    public void Tokenize_TagNamesAreLowercase()
    {
        var tokenizer = new BbTokenizer("[BOLD]text[/BOLD]");
        var tokens = tokenizer.Tokenize();

        tokens[0].Should().BeOfType<OpenTagToken>().Which.Name.Should().Be("bold");
        tokens[2].Should().BeOfType<CloseTagToken>().Which.Name.Should().Be("bold");
    }

    [Fact]
    public void Tokenize_InvalidTag_TreatedAsText()
    {
        var tokenizer = new BbTokenizer("[not a tag");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        tokens[0].Should().BeOfType<TextToken>().Which.Content.Should().Be("[not a tag");
    }

    [Fact]
    public void Tokenize_Greentext_AtStartOfInput()
    {
        var tokenizer = new BbTokenizer(">this is greentext");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(2);
        tokens[0].Should().BeOfType<GreentextToken>();
        tokens[1].Should().BeOfType<TextToken>()
            .Which.Content.Should().Be("this is greentext");
    }

    [Fact]
    public void Tokenize_Greentext_AfterNewline()
    {
        var tokenizer = new BbTokenizer("normal\n>greentext");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(4);
        tokens[0].Should().BeOfType<TextToken>().Which.Content.Should().Be("normal");
        tokens[1].Should().BeOfType<NewlineToken>();
        tokens[2].Should().BeOfType<GreentextToken>();
        tokens[3].Should().BeOfType<TextToken>().Which.Content.Should().Be("greentext");
    }

    [Fact]
    public void Tokenize_GreaterThan_NotAtStartOfLine_TreatedAsText()
    {
        var tokenizer = new BbTokenizer("5 > 3");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(1);
        tokens[0].Should().BeOfType<TextToken>().Which.Content.Should().Be("5 > 3");
    }

    [Fact]
    public void Tokenize_Orangetext_AtStartOfLine()
    {
        var tokenizer = new BbTokenizer("<orangetext");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(2);
        tokens[0].Should().BeOfType<OrangetextToken>();
        tokens[1].Should().BeOfType<TextToken>()
            .Which.Content.Should().Be("orangetext");
    }

    [Fact]
    public void Tokenize_Redtext_DoubleEquals()
    {
        var tokenizer = new BbTokenizer("==red text==");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(3);
        tokens[0].Should().BeOfType<OpenTagToken>().Which.Name.Should().Be("redtext");
        tokens[1].Should().BeOfType<TextToken>().Which.Content.Should().Be("red text");
        tokens[2].Should().BeOfType<CloseTagToken>().Which.Name.Should().Be("redtext");
    }

    [Fact]
    public void Tokenize_Bluetext_DoubleDash()
    {
        var tokenizer = new BbTokenizer("--blue text--");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(3);
        tokens[0].Should().BeOfType<OpenTagToken>().Which.Name.Should().Be("bluetext");
        tokens[1].Should().BeOfType<TextToken>().Which.Content.Should().Be("blue text");
        tokens[2].Should().BeOfType<CloseTagToken>().Which.Name.Should().Be("bluetext");
    }

    [Fact]
    public void Tokenize_Glow_DoublePercent()
    {
        var tokenizer = new BbTokenizer("%%glowing%%");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(3);
        tokens[0].Should().BeOfType<OpenTagToken>().Which.Name.Should().Be("glowtext");
        tokens[1].Should().BeOfType<TextToken>().Which.Content.Should().Be("glowing");
        tokens[2].Should().BeOfType<CloseTagToken>().Which.Name.Should().Be("glowtext");
    }

    [Fact]
    public void Tokenize_RedGlow_DoubleExclamation()
    {
        var tokenizer = new BbTokenizer("!!red glow!!");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(3);
        tokens[0].Should().BeOfType<OpenTagToken>().Which.Name.Should().Be("redglow");
        tokens[1].Should().BeOfType<TextToken>().Which.Content.Should().Be("red glow");
        tokens[2].Should().BeOfType<CloseTagToken>().Which.Name.Should().Be("redglow");
    }

    [Fact]
    public void Tokenize_YellowGlow_DoubleColon()
    {
        var tokenizer = new BbTokenizer("::yellow glow::");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(3);
        tokens[0].Should().BeOfType<OpenTagToken>().Which.Name.Should().Be("yellowglow");
        tokens[1].Should().BeOfType<TextToken>().Which.Content.Should().Be("yellow glow");
        tokens[2].Should().BeOfType<CloseTagToken>().Which.Name.Should().Be("yellowglow");
    }

    [Fact]
    public void Tokenize_Rainbow_TildePattern()
    {
        var tokenizer = new BbTokenizer("~-~rainbow~-~");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(3);
        tokens[0].Should().BeOfType<OpenTagToken>().Which.Name.Should().Be("rainbow");
        tokens[1].Should().BeOfType<TextToken>().Which.Content.Should().Be("rainbow");
        tokens[2].Should().BeOfType<CloseTagToken>().Which.Name.Should().Be("rainbow");
    }

    [Fact]
    public void Tokenize_InlinePattern_DoesNotSpanNewlines()
    {
        var tokenizer = new BbTokenizer("==no closing\nnewline==");
        var tokens = tokenizer.Tokenize();

        // Should not match as redtext because it spans newlines
        tokens.Should().Contain(t => t is TextToken);
        tokens.Should().NotContain(t => t is OpenTagToken && ((OpenTagToken)t).Name == "redtext");
    }

    [Fact]
    public void Tokenize_MixedContent()
    {
        var tokenizer = new BbTokenizer("Hello [b]world[/b]!\n>greentext");
        var tokens = tokenizer.Tokenize();

        tokens.Should().HaveCount(8);
        tokens[0].Should().BeOfType<TextToken>().Which.Content.Should().Be("Hello ");
        tokens[1].Should().BeOfType<OpenTagToken>().Which.Name.Should().Be("b");
        tokens[2].Should().BeOfType<TextToken>().Which.Content.Should().Be("world");
        tokens[3].Should().BeOfType<CloseTagToken>().Which.Name.Should().Be("b");
        tokens[4].Should().BeOfType<TextToken>().Which.Content.Should().Be("!");
        tokens[5].Should().BeOfType<NewlineToken>();
        tokens[6].Should().BeOfType<GreentextToken>();
        tokens[7].Should().BeOfType<TextToken>().Which.Content.Should().Be("greentext");
    }

    [Fact]
    public void Tokenize_EmptyInput_ReturnsEmptyList()
    {
        var tokenizer = new BbTokenizer("");
        var tokens = tokenizer.Tokenize();

        tokens.Should().BeEmpty();
    }

    [Fact]
    public void Tokenize_EmptyGreentext_EmitsMarkerThenNewline()
    {
        var tokenizer = new BbTokenizer(">\n");
        var tokens = tokenizer.Tokenize();

        // Marker-only token is always emitted; parser handles empty fallback
        tokens.Should().HaveCount(2);
        tokens[0].Should().BeOfType<GreentextToken>();
        tokens[1].Should().BeOfType<NewlineToken>();
    }
}

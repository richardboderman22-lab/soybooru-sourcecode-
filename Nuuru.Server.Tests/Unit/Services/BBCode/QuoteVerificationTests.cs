using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Nuuru.Server.Services.BBCode;

namespace Nuuru.Server.Tests.Unit.Services.BBCode;

/// <summary>
/// Tests the full quote generation → re-parse → verification pipeline.
/// Covers every BBCode feature that can appear inside a quote and verifies
/// that (a) the quote structure stays intact and (b) the HMAC hash matches.
/// </summary>
public class QuoteVerificationTests
{
    private readonly BBCodeService _bbCodeService = new();
    private readonly QuoteChecksumService _checksumService;

    public QuoteVerificationTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BBCode:QuoteSigningKey"] = "test-signing-key-for-unit-tests"
            })
            .Build();
        _checksumService = new QuoteChecksumService(config);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Simulates the quote generation endpoint: wraps raw BBCode in a
    /// [quote] tag with a hash derived from the extracted plain text.
    /// </summary>
    private string GenerateQuoteBBCode(
        string contentRaw, string sourceType = "comment", string sourceId = "1")
    {
        var plainText = _bbCodeService.ExtractPlainText(contentRaw);
        var hash = _checksumService.GenerateHash(sourceType, sourceId, plainText);
        return $"[quote {sourceType}Id={sourceId} author=TestUser hash={hash}]{contentRaw}[/quote]";
    }

    /// <summary>
    /// Full round-trip: generate quote → re-parse → verify hash.
    /// Returns true if the renderer would mark the quote as verified.
    /// </summary>
    private bool VerifyRoundTrip(
        string contentRaw, string sourceType = "comment", string sourceId = "1")
    {
        var quoteBBCode = GenerateQuoteBBCode(contentRaw, sourceType, sourceId);

        var tokenizer = new BbTokenizer(quoteBBCode);
        var tokens = tokenizer.Tokenize();
        var parser = new BbParser(tokens);
        var ast = parser.Parse();

        var quoteNode = ast.OfType<QuoteNode>().FirstOrDefault();
        if (quoteNode == null) return false;

        var extractedText = ExtractTextContent(quoteNode.Children);
        return _checksumService.VerifyHash(
            sourceType, sourceId, extractedText, quoteNode.ProvidedHash!);
    }

    /// <summary>
    /// Parses the generated quote BBCode and returns the QuoteNode, or null
    /// if the quote structure was broken (e.g. close tag eaten by tokenizer).
    /// </summary>
    private QuoteNode? ParseQuoteNode(string contentRaw)
    {
        var quoteBBCode = GenerateQuoteBBCode(contentRaw);
        var tokenizer = new BbTokenizer(quoteBBCode);
        var tokens = tokenizer.Tokenize();
        var parser = new BbParser(tokens);
        var ast = parser.Parse();
        return ast.OfType<QuoteNode>().FirstOrDefault();
    }

    /// <summary>
    /// Mirrors Renderer.ExtractTextContent — extracts plain text from AST nodes.
    /// </summary>
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

    /// <summary>
    /// Asserts that the [/quote] close tag is NOT consumed by
    /// a greedy tokenizer pattern and the quote structure is intact.
    /// </summary>
    private void AssertQuoteStructureIntact(string contentRaw)
    {
        var quoteNode = ParseQuoteNode(contentRaw);
        quoteNode.Should().NotBeNull(
            $"[/quote] should not be consumed by content: {contentRaw}");

        var childText = ExtractTextContent(quoteNode!.Children);
        childText.Should().NotContain("[/quote]",
            "the close tag should not appear as literal text inside the quote");
    }

    /// <summary>
    /// Asserts content after the quote is not swallowed into the quote.
    /// </summary>
    private void AssertReplyNotSwallowed(string contentRaw)
    {
        var quoteBBCode = GenerateQuoteBBCode(contentRaw);
        var fullComment = quoteBBCode + "\nmy reply here";

        var tokenizer = new BbTokenizer(fullComment);
        var tokens = tokenizer.Tokenize();
        var parser = new BbParser(tokens);
        var ast = parser.Parse();

        var topLevelText = ExtractTextContent(
            ast.Where(n => n is not QuoteNode).ToList());
        topLevelText.Should().Contain("my reply here",
            "the reply text must not be swallowed into the quote");
    }

    // ── Plain text (baseline) ──────────────────────────────────────

    [Fact]
    public void PlainText_Verifies()
    {
        VerifyRoundTrip("hello world").Should().BeTrue();
    }

    [Fact]
    public void MultilinePlainText_Verifies()
    {
        VerifyRoundTrip("line one\nline two\nline three").Should().BeTrue();
    }

    [Fact]
    public void PlainText_WithSpecialChars_Verifies()
    {
        VerifyRoundTrip("test & \"quotes\" 'apostrophes' <angles>").Should().BeTrue();
    }

    // ── Greentext ──────────────────────────────────────────────────

    [Fact]
    public void Greentext_SingleLine_Verifies()
    {
        VerifyRoundTrip(">hello").Should().BeTrue();
    }

    [Fact]
    public void Greentext_AfterNewline_Verifies()
    {
        VerifyRoundTrip("normal\n>greentext").Should().BeTrue();
    }

    [Fact]
    public void Greentext_MultipleLines_Verifies()
    {
        VerifyRoundTrip(">line one\n>line two\n>line three").Should().BeTrue();
    }

    [Fact]
    public void Greentext_AfterNewline_StructureIntact()
    {
        AssertQuoteStructureIntact("normal\n>greentext");
    }

    [Fact]
    public void Greentext_AfterNewline_DoesNotSwallowReply()
    {
        AssertReplyNotSwallowed("normal\n>greentext");
    }

    [Fact]
    public void Greentext_OnlyLine_StructureIntact()
    {
        AssertQuoteStructureIntact(">greentext");
    }

    // ── Orangetext ─────────────────────────────────────────────────

    [Fact]
    public void Orangetext_SingleLine_Verifies()
    {
        VerifyRoundTrip("<orangetext").Should().BeTrue();
    }

    [Fact]
    public void Orangetext_AfterNewline_Verifies()
    {
        VerifyRoundTrip("normal\n<orangetext").Should().BeTrue();
    }

    [Fact]
    public void Orangetext_AfterNewline_StructureIntact()
    {
        AssertQuoteStructureIntact("normal\n<orangetext");
    }

    [Fact]
    public void Orangetext_AfterNewline_DoesNotSwallowReply()
    {
        AssertReplyNotSwallowed("normal\n<orangetext");
    }

    // ── Redtext ────────────────────────────────────────────────────

    [Fact]
    public void Redtext_Verifies()
    {
        VerifyRoundTrip("==important==").Should().BeTrue();
    }

    [Fact]
    public void Redtext_AfterNewline_Verifies()
    {
        VerifyRoundTrip("normal\n==important==").Should().BeTrue();
    }

    [Fact]
    public void Redtext_BeforeCloseTag_StructureIntact()
    {
        AssertQuoteStructureIntact("normal\n==important==");
    }

    // ── Bluetext ───────────────────────────────────────────────────

    [Fact]
    public void Bluetext_Verifies()
    {
        VerifyRoundTrip("--blue--").Should().BeTrue();
    }

    [Fact]
    public void Bluetext_AfterNewline_Verifies()
    {
        VerifyRoundTrip("normal\n--blue--").Should().BeTrue();
    }

    [Fact]
    public void Bluetext_BeforeCloseTag_StructureIntact()
    {
        AssertQuoteStructureIntact("normal\n--blue--");
    }

    // ── Glow ───────────────────────────────────────────────────────

    [Fact]
    public void Glow_Verifies()
    {
        VerifyRoundTrip("%%glow%%").Should().BeTrue();
    }

    [Fact]
    public void Glow_AfterNewline_StructureIntact()
    {
        AssertQuoteStructureIntact("normal\n%%glow%%");
    }

    // ── Red glow ───────────────────────────────────────────────────

    [Fact]
    public void RedGlow_Verifies()
    {
        VerifyRoundTrip("!!redglow!!").Should().BeTrue();
    }

    [Fact]
    public void RedGlow_AfterNewline_StructureIntact()
    {
        AssertQuoteStructureIntact("normal\n!!redglow!!");
    }

    // ── Yellow glow ────────────────────────────────────────────────

    [Fact]
    public void YellowGlow_Verifies()
    {
        VerifyRoundTrip("::yellow::").Should().BeTrue();
    }

    [Fact]
    public void YellowGlow_AfterNewline_StructureIntact()
    {
        AssertQuoteStructureIntact("normal\n::yellow::");
    }

    // ── Rainbow ────────────────────────────────────────────────────

    [Fact]
    public void Rainbow_Verifies()
    {
        VerifyRoundTrip("~-~rainbow~-~").Should().BeTrue();
    }

    [Fact]
    public void Rainbow_AfterNewline_StructureIntact()
    {
        AssertQuoteStructureIntact("normal\n~-~rainbow~-~");
    }

    // ── Formatting tags ────────────────────────────────────────────

    [Fact]
    public void Bold_Verifies()
    {
        VerifyRoundTrip("[b]bold text[/b]").Should().BeTrue();
    }

    [Fact]
    public void Italic_Verifies()
    {
        VerifyRoundTrip("[i]italic text[/i]").Should().BeTrue();
    }

    [Fact]
    public void Strike_Verifies()
    {
        VerifyRoundTrip("[s]strikethrough[/s]").Should().BeTrue();
    }

    [Fact]
    public void Underline_Verifies()
    {
        VerifyRoundTrip("[u]underlined[/u]").Should().BeTrue();
    }

    [Fact]
    public void NestedFormatting_Verifies()
    {
        VerifyRoundTrip("[b][i]bold italic[/i][/b]").Should().BeTrue();
    }

    [Fact]
    public void Bold_StructureIntact()
    {
        AssertQuoteStructureIntact("[b]bold text[/b]");
    }

    // ── Code blocks ────────────────────────────────────────────────

    [Fact]
    public void Code_Verifies()
    {
        VerifyRoundTrip("[code]console.log('hello')[/code]").Should().BeTrue();
    }

    [Fact]
    public void InlineCode_Verifies()
    {
        VerifyRoundTrip("[inline]code[/inline]").Should().BeTrue();
    }

    [Fact]
    public void Code_WithGreentextInside_Verifies()
    {
        // Code blocks should preserve > literally
        VerifyRoundTrip("[code]>not greentext[/code]").Should().BeTrue();
    }

    [Fact]
    public void Code_StructureIntact()
    {
        AssertQuoteStructureIntact("[code]some code[/code]");
    }

    // ── Spoiler / Blur ─────────────────────────────────────────────

    [Fact]
    public void Spoiler_Verifies()
    {
        VerifyRoundTrip("[spoiler]hidden content[/spoiler]").Should().BeTrue();
    }

    [Fact]
    public void Blur_Verifies()
    {
        VerifyRoundTrip("[blur]blurred[/blur]").Should().BeTrue();
    }

    [Fact]
    public void Spoiler_StructureIntact()
    {
        AssertQuoteStructureIntact("[spoiler]hidden[/spoiler]");
    }

    // ── Headings ───────────────────────────────────────────────────

    [Fact]
    public void Heading_Verifies()
    {
        VerifyRoundTrip("[h1]Title[/h1]").Should().BeTrue();
    }

    [Fact]
    public void AllHeadings_Verify()
    {
        for (int i = 1; i <= 4; i++)
        {
            VerifyRoundTrip($"[h{i}]heading {i}[/h{i}]").Should().BeTrue(
                $"h{i} should verify");
        }
    }

    // ── URLs ───────────────────────────────────────────────────────

    [Fact]
    public void Url_WithAttribute_Verifies()
    {
        VerifyRoundTrip("[url=https://example.com]link[/url]").Should().BeTrue();
    }

    [Fact]
    public void Url_WithoutAttribute_Verifies()
    {
        VerifyRoundTrip("[url]https://example.com[/url]").Should().BeTrue();
    }

    [Fact]
    public void Url_StructureIntact()
    {
        AssertQuoteStructureIntact("[url=https://example.com]link text[/url]");
    }

    // ── Thumbs ─────────────────────────────────────────────────────

    [Fact]
    public void Thumb_Verifies()
    {
        VerifyRoundTrip("[thumb]123[/thumb]").Should().BeTrue();
    }

    [Fact]
    public void Thumb_StructureIntact()
    {
        AssertQuoteStructureIntact("[thumb]123[/thumb]");
    }

    // ── Mentions ───────────────────────────────────────────────────

    [Fact]
    public void Mention_Verifies()
    {
        var guid = Guid.NewGuid();
        VerifyRoundTrip($"[mention userguid={guid}]@TestUser[/mention]").Should().BeTrue();
    }

    [Fact]
    public void Mention_StructureIntact()
    {
        var guid = Guid.NewGuid();
        AssertQuoteStructureIntact($"[mention userguid={guid}]@TestUser[/mention]");
    }

    // ── Lists ──────────────────────────────────────────────────────

    [Fact]
    public void UnorderedList_Verifies()
    {
        VerifyRoundTrip("[list][*]item one[/*][*]item two[/*][/list]").Should().BeTrue();
    }

    [Fact]
    public void OrderedList_Verifies()
    {
        VerifyRoundTrip("[list=1][*]first[/*][*]second[/*][/list]").Should().BeTrue();
    }

    [Fact]
    public void List_StructureIntact()
    {
        AssertQuoteStructureIntact("[list][*]item[/*][/list]");
    }

    // ── Color / Size / Font ────────────────────────────────────────

    [Fact]
    public void Color_Verifies()
    {
        VerifyRoundTrip("[color=red]colored text[/color]").Should().BeTrue();
    }

    [Fact]
    public void Size_Verifies()
    {
        VerifyRoundTrip("[size=120%]big text[/size]").Should().BeTrue();
    }

    [Fact]
    public void Font_Verifies()
    {
        VerifyRoundTrip("[font=Arial]styled[/font]").Should().BeTrue();
    }

    // ── Align ──────────────────────────────────────────────────────

    [Fact]
    public void Align_Verifies()
    {
        VerifyRoundTrip("[align=center]centered[/align]").Should().BeTrue();
    }

    // ── Sub / Sup ──────────────────────────────────────────────────

    [Fact]
    public void SubSup_Verifies()
    {
        VerifyRoundTrip("H[sub]2[/sub]O and x[sup]2[/sup]").Should().BeTrue();
    }

    // ── Mixed content (realistic scenarios) ────────────────────────

    [Fact]
    public void MixedInlinePatterns_Verifies()
    {
        VerifyRoundTrip(">greentext\n==redtext==\n--bluetext--").Should().BeTrue();
    }

    [Fact]
    public void MixedFormattingAndInline_Verifies()
    {
        VerifyRoundTrip("[b]bold[/b]\n>greentext\nnormal text").Should().BeTrue();
    }

    [Fact]
    public void MixedFormattingAndInline_StructureIntact()
    {
        AssertQuoteStructureIntact("[b]bold[/b]\n>greentext\nnormal text");
    }

    [Fact]
    public void MixedFormattingAndInline_DoesNotSwallowReply()
    {
        AssertReplyNotSwallowed("[b]bold[/b]\n>greentext\nnormal text");
    }

    [Fact]
    public void ComplexRealisticContent_Verifies()
    {
        var content = """
            Hello [b]world[/b]!
            >implying this works
            ==important notice==
            [url=https://example.com]see here[/url]
            [spoiler]hidden[/spoiler]
            """;
        VerifyRoundTrip(content).Should().BeTrue();
    }

    [Fact]
    public void ComplexRealisticContent_StructureIntact()
    {
        var content = """
            Hello [b]world[/b]!
            >implying this works
            ==important notice==
            [url=https://example.com]see here[/url]
            """;
        AssertQuoteStructureIntact(content);
    }

    [Fact]
    public void EveryInlineOnLastLine_StructureIntact()
    {
        // Each inline pattern on the last line (no trailing newline)
        // to test that none of them eat [/quote]
        var patterns = new[]
        {
            "text\n>greentext",
            "text\n<orangetext",
            "text\n==redtext==",
            "text\n--bluetext--",
            "text\n%%glow%%",
            "text\n!!redglow!!",
            "text\n::yellow::",
            "text\n~-~rainbow~-~",
        };

        foreach (var pattern in patterns)
        {
            AssertQuoteStructureIntact(pattern);
        }
    }

    [Fact]
    public void EveryInlineOnLastLine_Verifies()
    {
        var patterns = new[]
        {
            "text\n>greentext",
            "text\n<orangetext",
            "text\n==redtext==",
            "text\n--bluetext--",
            "text\n%%glow%%",
            "text\n!!redglow!!",
            "text\n::yellow::",
            "text\n~-~rainbow~-~",
        };

        foreach (var pattern in patterns)
        {
            VerifyRoundTrip(pattern).Should().BeTrue(
                $"pattern should verify: {pattern}");
        }
    }

    [Fact]
    public void EveryInlineOnLastLine_DoesNotSwallowReply()
    {
        var patterns = new[]
        {
            "text\n>greentext",
            "text\n<orangetext",
            "text\n==redtext==",
            "text\n--bluetext--",
            "text\n%%glow%%",
            "text\n!!redglow!!",
            "text\n::yellow::",
            "text\n~-~rainbow~-~",
        };

        foreach (var pattern in patterns)
        {
            AssertReplyNotSwallowed(pattern);
        }
    }

    // ── Nested quotes ──────────────────────────────────────────────

    [Fact]
    public void NestedQuote_OuterVerifies()
    {
        var innerQuote = GenerateQuoteBBCode("inner content", "comment", "2");
        var outerContent = $"{innerQuote}\nouter reply";
        VerifyRoundTrip(outerContent).Should().BeTrue();
    }

    [Fact]
    public void NestedQuote_WithGreentext_OuterVerifies()
    {
        var innerQuote = GenerateQuoteBBCode(">greentext inside", "comment", "2");
        var outerContent = $"{innerQuote}\nouter reply";
        VerifyRoundTrip(outerContent).Should().BeTrue();
    }

    [Fact]
    public void NestedQuote_StructureIntact()
    {
        var innerQuote = GenerateQuoteBBCode("inner content", "comment", "2");
        AssertQuoteStructureIntact($"{innerQuote}\nouter text");
    }

    [Fact]
    public void DoubleNestedQuote_Verifies()
    {
        var innermost = GenerateQuoteBBCode("deep content", "comment", "3");
        var middle = GenerateQuoteBBCode($"{innermost}\nmiddle text", "comment", "2");
        var outerContent = $"{middle}\nouter text";
        VerifyRoundTrip(outerContent).Should().BeTrue();
    }

    // ── Simple (unverified) quotes ─────────────────────────────────

    [Fact]
    public void SimpleQuote_WithGreentext_ParsesCorrectly()
    {
        var bbcode = "[quote=SomeUser]>greentext[/quote]";
        var tokenizer = new BbTokenizer(bbcode);
        var tokens = tokenizer.Tokenize();
        var parser = new BbParser(tokens);
        var ast = parser.Parse();

        var quoteNode = ast.OfType<QuoteNode>().FirstOrDefault();
        quoteNode.Should().NotBeNull();
    }

    [Fact]
    public void SimpleQuote_WithGreentextAfterNewline_ParsesCorrectly()
    {
        var bbcode = "[quote=SomeUser]hello\n>greentext[/quote]";
        var tokenizer = new BbTokenizer(bbcode);
        var tokens = tokenizer.Tokenize();
        var parser = new BbParser(tokens);
        var ast = parser.Parse();

        var quoteNode = ast.OfType<QuoteNode>().FirstOrDefault();
        quoteNode.Should().NotBeNull(
            "greentext tokenizer should not eat [/quote]");
    }

    // ── Forum context quotes ───────────────────────────────────────

    [Fact]
    public void ForumQuote_Verifies()
    {
        VerifyRoundTrip("forum post content", "forum", "42").Should().BeTrue();
    }

    [Fact]
    public void ForumQuote_WithGreentext_Verifies()
    {
        VerifyRoundTrip(">greentext in forum", "forum", "42").Should().BeTrue();
    }

    // ── Edge cases ─────────────────────────────────────────────────

    [Fact]
    public void EmptyContent_Verifies()
    {
        VerifyRoundTrip("").Should().BeTrue();
    }

    [Fact]
    public void WhitespaceOnly_Verifies()
    {
        VerifyRoundTrip("   ").Should().BeTrue();
    }

    [Fact]
    public void SingleNewline_Verifies()
    {
        VerifyRoundTrip("\n").Should().BeTrue();
    }

    [Fact]
    public void MultipleNewlines_Verifies()
    {
        VerifyRoundTrip("\n\n\n").Should().BeTrue();
    }

    [Fact]
    public void GreentextFollowedByNewline_Verifies()
    {
        // Trailing newline means [/quote] is on its own line — no eating
        VerifyRoundTrip(">greentext\n").Should().BeTrue();
    }

    [Fact]
    public void ContentWithBrackets_Verifies()
    {
        // Square brackets that aren't valid tags
        VerifyRoundTrip("array[0] = value[1]").Should().BeTrue();
    }

    [Fact]
    public void ContentWithAngleBrackets_Verifies()
    {
        // HTML-like content that's not at line start (not orangetext)
        VerifyRoundTrip("use List<int> for generics").Should().BeTrue();
    }

    [Fact]
    public void GreentextImmediatelyAfterOpenTag_Verifies()
    {
        // [quote ...]>text[/quote] — the > is right after the tag
        // atLineStart is false after the tag, so this tests whether
        // > is still handled consistently for verification
        VerifyRoundTrip(">first line only").Should().BeTrue();
    }
}

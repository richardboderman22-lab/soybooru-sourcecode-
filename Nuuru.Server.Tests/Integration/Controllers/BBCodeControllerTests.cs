using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nuuru.Server.Data;
using Nuuru.Server.DTOs.BBCode;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Integration.Controllers;

public class BBCodeControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _dbName;

    public BBCodeControllerTests(CustomWebApplicationFactory<Program> factory)
    {
        _dbName = $"nuuru_test_{Guid.NewGuid():N}";
        _factory = factory;
        _factory.DatabaseName = _dbName;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Parse_DeeplyNestedVerifiedQuotes_ReturnsAllLevels()
    {
        // Arrange - exact reproduction of the failing input
        var request = new
        {
            content = "[quote postId=22630 author=Obsidian hash=9c6cbc2b][quote postId=22626 author=Nabbv hash=5296cce3][quote postId=22621 author=Obsidian hash=9946481f][quote postId=22613 author=Nabbv hash=66b93d68]>Nigger I have seen hands and you ARE NOT white just because you have a light tone does NOT MAKE YOU white nor will you ever be cuckjeet\n>being white does NOT make you white\nthe state of amerimutts\n[quote postId=22607 author=jimbo hash=4d80e8f1][quote postId=22589 author=Nabbv hash=17a80fe5][quote postId=22587 author=jimbo hash=c54202f1][quote postId=22580 author=Nabbv hash=042b2d25][quote postId=22566 author=jimbo hash=9515449b][quote postId=22564 author=Nabbv hash=8f7232a6][quote postId=22562 author=Obsidian hash=4b89294d][quote postId=22558 author=Nabbv hash=276a369f][quote postId=22555 author=Obsidian hash=8bb76449][quote postId=22549 author=Nabbv hash=d0d0327a][quote postId=22548 author=Obsidian hash=0349f28e]Foreskin -Goy\nCircumcized foreskin - Goy master \nNo foreskin - Messiah???\n[/quote]\nonly amerimutts would care about having a foreskin\n[/quote]\nSnca foreskin debate is for fucking retards\n[/quote]\nWhy wouldn't you want a foreskin though?\n[/quote]\n>Why wouldn't you want foreskin though?\nNigger why do you care if someone has fucking foreskin or not?\n[/quote]\nOnly a retard who HAS been circumcised would cope like this LMFAO\n[/quote]\nGenuinely what benefits are there to not being circumcised foreskin just looks ugly imo\n[/quote]\nIt's more sensitive and you feel more. Masturbation/sex feels about 3 times gooder. \n[/quote]\nAny studies on this?\n[/quote]\nonly anecdotes from those who have been circumcised later in life. because their dickhead rubs up against their boxers or whatever, it becomes desensitized. plus it doesn't create lubrication as well\n[/quote]\nI see, damn gotta add that to the list of reasons to hate jews.\n[/quote]\nit's sad but I heard you can regrow it\n[/quote]\nCalling me a amerimutt even doe I don't live in the muttland. Cuckjeet you aren't white nor will you ever be \n[/quote]\nI'm not white I just have white skin and blue eyes\n[/quote]\nYou have neither of what you just said, your a shit skin with t50 eyes [/quote]",
            context = "forum"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/bbcode/parse", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ParseResponse>();
        result.Should().NotBeNull();
        result!.Nodes.Should().HaveCount(1);

        // Walk all 15 levels of nested verified quotes
        var expectedPostIds = new[] { "22630", "22626", "22621", "22613", "22607", "22589", "22587", "22580", "22566", "22564", "22562", "22558", "22555", "22549", "22548" };

        var current = result.Nodes[0].Should().BeOfType<QuoteNodeDto>().Subject;
        for (var i = 0; i < expectedPostIds.Length; i++)
        {
            current.SourceId.Should().Be(expectedPostIds[i], $"quote nesting level {i}");
            current.SourceType.Should().Be("forum");

            if (i < expectedPostIds.Length - 1)
            {
                var nestedQuote = current.Children.OfType<QuoteNodeDto>().FirstOrDefault();
                nestedQuote.Should().NotBeNull($"expected nested quote at level {i + 1}");
                current = nestedQuote!;
            }
        }

        // Innermost quote should contain the text
        current.Children.OfType<TextNodeDto>().Should().Contain(t => t.Content.Contains("Foreskin"));
    }
}

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.Data;
using Nuuru.Server.Models.Booru;
using Nuuru.Server.Services;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class VoteServiceTests
{
    private static readonly Guid User1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid User2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ApplicationDbContext _context;
    private readonly VoteService _sut;

    public VoteServiceTests()
    {
        _context = TestDbContextFactory.CreateWithSeedData();
        _sut = new VoteService(_context, Mock.Of<ILogger<VoteService>>());
    }

    // Post 1 is uploaded by User1, Post 2 by User2 (from seed data)

    [Fact]
    public async Task Upvote_IncreasesUploaderReactionScore()
    {
        var uploaderBefore = await _context.Users.FindAsync(User1Id);
        var scoreBefore = uploaderBefore!.ReactionScore;

        await _sut.VoteAsync(1, User2Id, 1); // User2 upvotes User1's post

        await _context.Entry(uploaderBefore).ReloadAsync();
        uploaderBefore.ReactionScore.Should().Be(scoreBefore + 1);
    }

    [Fact]
    public async Task Downvote_DecreasesUploaderReactionScore()
    {
        var uploaderBefore = await _context.Users.FindAsync(User1Id);
        var scoreBefore = uploaderBefore!.ReactionScore;

        await _sut.VoteAsync(1, User2Id, -1); // User2 downvotes User1's post

        await _context.Entry(uploaderBefore).ReloadAsync();
        uploaderBefore.ReactionScore.Should().Be(scoreBefore - 1);
    }

    [Fact]
    public async Task RemoveVote_RevertsUploaderReactionScore()
    {
        var uploader = await _context.Users.FindAsync(User1Id);
        var scoreBefore = uploader!.ReactionScore;

        await _sut.VoteAsync(1, User2Id, 1); // upvote
        await _sut.VoteAsync(1, User2Id, 0); // remove

        await _context.Entry(uploader).ReloadAsync();
        uploader.ReactionScore.Should().Be(scoreBefore);
    }

    [Fact]
    public async Task ChangeVote_UpToDown_AdjustsReactionScoreByTwo()
    {
        var uploader = await _context.Users.FindAsync(User1Id);
        var scoreBefore = uploader!.ReactionScore;

        await _sut.VoteAsync(1, User2Id, 1);  // upvote (+1)
        await _sut.VoteAsync(1, User2Id, -1); // change to downvote (net -2)

        await _context.Entry(uploader).ReloadAsync();
        uploader.ReactionScore.Should().Be(scoreBefore - 1);
    }

    [Fact]
    public async Task SelfVote_DoesNotAffectReactionScore()
    {
        var uploader = await _context.Users.FindAsync(User1Id);
        var scoreBefore = uploader!.ReactionScore;

        await _sut.VoteAsync(1, User1Id, 1); // User1 upvotes own post

        await _context.Entry(uploader).ReloadAsync();
        uploader.ReactionScore.Should().Be(scoreBefore);
    }

    [Fact]
    public async Task RemoveNonexistentVote_DoesNotChangeScore()
    {
        var uploader = await _context.Users.FindAsync(User1Id);
        var scoreBefore = uploader!.ReactionScore;

        await _sut.VoteAsync(1, User2Id, 0); // remove vote that doesn't exist

        await _context.Entry(uploader).ReloadAsync();
        uploader.ReactionScore.Should().Be(scoreBefore);
    }
}

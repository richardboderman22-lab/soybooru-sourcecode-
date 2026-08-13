using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nuuru.Server.Data;
using Nuuru.Server.Models.Messaging;
using Nuuru.Server.Services;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class ConversationServiceTests
{
    [Fact]
    public async Task RemoveParticipantAsync_RemovesParticipantAndEvictsFromVoice()
    {
        await using var fixture = await ConversationServiceTestFixture.CreateAsync();
        var creator = MockData.CreateTestUser("creator", "creator@example.com", Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var removedUser = MockData.CreateTestUser("removed", "removed@example.com", Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var conversation = await fixture.SeedConversationAsync(creator, removedUser);
        var service = fixture.CreateService();

        var removed = await service.RemoveParticipantAsync(conversation.Id, creator.Id, removedUser.Id);

        removed.Should().BeTrue();
        fixture.Context.ConversationParticipants
            .Any(participant => participant.ConversationId == conversation.Id && participant.UserId == removedUser.Id && !participant.HasLeft)
            .Should().BeFalse();
        fixture.RoomAdminClient.RemovedParticipants.Should().ContainSingle();
        fixture.RoomAdminClient.RemovedParticipants[0].Should().Be((ConversationVoiceService.GetRoomName(conversation.Id), removedUser.Id.ToString()));
    }

    [Fact]
    public async Task LeaveConversationAsync_MarksParticipantAsLeftAndEvictsFromVoice()
    {
        await using var fixture = await ConversationServiceTestFixture.CreateAsync();
        var creator = MockData.CreateTestUser("creator", "creator@example.com", Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var leavingUser = MockData.CreateTestUser("leaving", "leaving@example.com", Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var conversation = await fixture.SeedConversationAsync(creator, leavingUser);
        var service = fixture.CreateService();

        var left = await service.LeaveConversationAsync(conversation.Id, leavingUser.Id);

        left.Should().BeTrue();
        var participant = await fixture.Context.ConversationParticipants.SingleAsync(currentParticipant =>
            currentParticipant.ConversationId == conversation.Id && currentParticipant.UserId == leavingUser.Id);
        participant.HasLeft.Should().BeTrue();
        participant.LeftAt.Should().NotBeNull();
        fixture.RoomAdminClient.RemovedParticipants.Should().ContainSingle();
        fixture.RoomAdminClient.RemovedParticipants[0].Should().Be((ConversationVoiceService.GetRoomName(conversation.Id), leavingUser.Id.ToString()));
    }

    [Fact]
    public async Task DeleteConversationAsync_DeletesConversationAndEvictsAllParticipants()
    {
        await using var fixture = await ConversationServiceTestFixture.CreateAsync();
        var creator = MockData.CreateTestUser("creator", "creator@example.com", Guid.Parse("55555555-5555-5555-5555-555555555555"));
        var participantA = MockData.CreateTestUser("participant-a", "participant-a@example.com", Guid.Parse("66666666-6666-6666-6666-666666666666"));
        var participantB = MockData.CreateTestUser("participant-b", "participant-b@example.com", Guid.Parse("77777777-7777-7777-7777-777777777777"));
        var conversation = await fixture.SeedConversationAsync(creator, participantA, participantB);
        var service = fixture.CreateService();

        var deleted = await service.DeleteConversationAsync(conversation.Id);

        deleted.Should().BeTrue();
        (await fixture.Context.Conversations.AnyAsync(currentConversation => currentConversation.Id == conversation.Id)).Should().BeFalse();
        fixture.RoomAdminClient.RemovedParticipants.Should().BeEquivalentTo(
        [
            (ConversationVoiceService.GetRoomName(conversation.Id), creator.Id.ToString()),
            (ConversationVoiceService.GetRoomName(conversation.Id), participantA.Id.ToString()),
            (ConversationVoiceService.GetRoomName(conversation.Id), participantB.Id.ToString())
        ]);
    }

    private sealed class ConversationServiceTestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ConversationServiceTestFixture(SqliteConnection connection, ApplicationDbContext context)
        {
            _connection = connection;
            Context = context;
            RoomAdminClient = new FakeLiveKitRoomAdminClient();
        }

        public ApplicationDbContext Context { get; }
        public FakeLiveKitRoomAdminClient RoomAdminClient { get; }

        public static async Task<ConversationServiceTestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            return new ConversationServiceTestFixture(connection, context);
        }

        public ConversationService CreateService()
        {
            return new ConversationService(
                Context,
                Mock.Of<IMessageService>(),
                Mock.Of<IUserRelationService>(),
                RoomAdminClient,
                NullLogger<ConversationService>.Instance);
        }

        public async Task<Conversation> SeedConversationAsync(params Nuuru.Server.Models.ApplicationUser[] users)
        {
            var creator = users[0];
            var conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                CreatorId = creator.Id,
                Creator = creator,
                Title = "Test Conversation"
            };

            Context.Users.AddRange(users);
            Context.Conversations.Add(conversation);

            var joinedAt = DateTime.UtcNow.AddMinutes(-users.Length);
            foreach (var user in users)
            {
                Context.ConversationParticipants.Add(new ConversationParticipant
                {
                    ConversationId = conversation.Id,
                    UserId = user.Id,
                    JoinedAt = joinedAt
                });
                joinedAt = joinedAt.AddMinutes(1);
            }

            await Context.SaveChangesAsync();
            return conversation;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakeLiveKitRoomAdminClient : ILiveKitRoomAdminClient
    {
        public bool IsEnabled => true;
        public List<(string RoomName, string ParticipantIdentity)> RemovedParticipants { get; } = [];

        public Task<IReadOnlyList<LiveKitRoomParticipant>> ListParticipantsAsync(string roomName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LiveKitRoomParticipant>>([]);
        }

        public Task<bool> SetParticipantDeafenedAsync(string roomName, string participantIdentity, bool deafened, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task RemoveParticipantAsync(string roomName, string participantIdentity, CancellationToken cancellationToken = default)
        {
            RemovedParticipants.Add((roomName, participantIdentity));
            return Task.CompletedTask;
        }
    }
}

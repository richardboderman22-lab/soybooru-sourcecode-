using FluentAssertions;
using Microsoft.Extensions.Options;
using Nuuru.Server.Data;
using Nuuru.Server.Models.Messaging;
using Nuuru.Server.Services;
using Nuuru.Server.Tests.Helpers;
using System.IdentityModel.Tokens.Jwt;

namespace Nuuru.Server.Tests.Unit.Services;

public class ConversationVoiceServiceTests
{
    [Fact]
    public async Task GetStateAsync_RequestingUserIsNotAnActiveParticipant_ReturnsNull()
    {
        using var context = TestDbContextFactory.Create();
        var creator = MockData.CreateTestUser("creator", "creator@example.com", Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var participant = MockData.CreateTestUser("participant", "participant@example.com", Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var outsider = MockData.CreateTestUser("outsider", "outsider@example.com", Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var conversation = CreateConversation(creator);

        context.Users.AddRange(creator, participant, outsider);
        context.Conversations.Add(conversation);
        context.ConversationParticipants.Add(new ConversationParticipant
        {
            ConversationId = conversation.Id,
            UserId = creator.Id
        });
        context.ConversationParticipants.Add(new ConversationParticipant
        {
            ConversationId = conversation.Id,
            UserId = participant.Id
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, new LiveKitOptions(), new FakeLiveKitRoomAdminClient(isEnabled: false));

        var state = await service.GetStateAsync(conversation.Id, outsider.Id);

        state.Should().BeNull();
    }

    [Fact]
    public async Task GetStateAsync_VoiceChatNotConfigured_ReturnsDisabledStateForParticipant()
    {
        using var context = TestDbContextFactory.Create();
        var creator = MockData.CreateTestUser("creator", "creator@example.com", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var conversation = await SeedConversationAsync(context, creator);
        var service = CreateService(context, new LiveKitOptions(), new FakeLiveKitRoomAdminClient(isEnabled: false));

        var state = await service.GetStateAsync(conversation.Id, creator.Id);

        state.Should().NotBeNull();
        state!.Enabled.Should().BeFalse();
        state.RoomName.Should().Be(ConversationVoiceService.GetRoomName(conversation.Id));
        state.ParticipantCount.Should().Be(0);
        state.Participants.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStateAsync_ConfiguredVoiceRoom_FiltersAndOrdersConnectedParticipants()
    {
        using var context = TestDbContextFactory.Create();
        var creator = MockData.CreateTestUser("creator", "creator@example.com", Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var participant = MockData.CreateTestUser("participant", "participant@example.com", Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var leftUser = MockData.CreateTestUser("left", "left@example.com", Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var outsider = MockData.CreateTestUser("outsider", "outsider@example.com", Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var conversation = CreateConversation(creator);
        var now = DateTime.UtcNow;

        context.Users.AddRange(creator, participant, leftUser, outsider);
        context.Conversations.Add(conversation);
        context.ConversationParticipants.AddRange(
            new ConversationParticipant
            {
                ConversationId = conversation.Id,
                UserId = creator.Id,
                JoinedAt = now.AddMinutes(-10)
            },
            new ConversationParticipant
            {
                ConversationId = conversation.Id,
                UserId = participant.Id,
                JoinedAt = now.AddMinutes(-5)
            },
            new ConversationParticipant
            {
                ConversationId = conversation.Id,
                UserId = leftUser.Id,
                JoinedAt = now.AddMinutes(-2),
                HasLeft = true,
                LeftAt = now.AddMinutes(-1)
            });
        await context.SaveChangesAsync();

        var adminClient = new FakeLiveKitRoomAdminClient(
            isEnabled: true,
            participants:
            [
                new LiveKitRoomParticipant("not-a-guid", "Ignore Me", null, null),
                new LiveKitRoomParticipant(outsider.Id.ToString(), outsider.UserName!, null, null),
                new LiveKitRoomParticipant(participant.Id.ToString(), "Participant Override", false, true),
                new LiveKitRoomParticipant(creator.Id.ToString(), string.Empty, true, false),
                new LiveKitRoomParticipant(leftUser.Id.ToString(), leftUser.UserName!, null, null)
            ]);

        var service = CreateService(context, ConfiguredOptions(), adminClient);

        var state = await service.GetStateAsync(conversation.Id, creator.Id);

        state.Should().NotBeNull();
        state!.Enabled.Should().BeTrue();
        state.RoomName.Should().Be(ConversationVoiceService.GetRoomName(conversation.Id));
        state.ParticipantCount.Should().Be(2);
        state.Participants.Select(p => p.UserId).Should().ContainInOrder(creator.Id, participant.Id);
        state.Participants[0].UserName.Should().Be("creator");
        state.Participants[0].IsMicrophoneEnabled.Should().BeTrue();
        state.Participants[0].IsDeafened.Should().BeFalse();
        state.Participants[1].UserName.Should().Be("Participant Override");
        state.Participants[1].IsMicrophoneEnabled.Should().BeFalse();
        state.Participants[1].IsDeafened.Should().BeTrue();
    }

    [Fact]
    public async Task CreateJoinTokenAsync_RequestingUserIsNotAnActiveParticipant_ReturnsNull()
    {
        using var context = TestDbContextFactory.Create();
        var creator = MockData.CreateTestUser("creator", "creator@example.com", Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var outsider = MockData.CreateTestUser("outsider", "outsider@example.com", Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var conversation = await SeedConversationAsync(context, creator);
        context.Users.Add(outsider);
        await context.SaveChangesAsync();

        var service = CreateService(context, ConfiguredOptions(), new FakeLiveKitRoomAdminClient(isEnabled: true));

        var token = await service.CreateJoinTokenAsync(conversation.Id, outsider.Id);

        token.Should().BeNull();
    }

    [Fact]
    public async Task CreateJoinTokenAsync_ActiveParticipant_ReturnsConversationScopedToken()
    {
        using var context = TestDbContextFactory.Create();
        var creator = MockData.CreateTestUser("creator", "creator@example.com", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var conversation = await SeedConversationAsync(context, creator);
        var service = CreateService(context, ConfiguredOptions(), new FakeLiveKitRoomAdminClient(isEnabled: true));

        var token = await service.CreateJoinTokenAsync(conversation.Id, creator.Id);

        token.Should().NotBeNull();
        token!.ServerUrl.Should().Be("wss://livekit.example.com");
        token.RoomName.Should().Be(ConversationVoiceService.GetRoomName(conversation.Id));
        token.Participant.UserId.Should().Be(creator.Id);
        token.Participant.UserName.Should().Be("creator");
        token.Token.Should().NotBeNullOrWhiteSpace();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);
        jwt.Claims.Should().Contain(claim => claim.Type == "sub" && claim.Value == creator.Id.ToString());
        jwt.Claims.Should().Contain(claim => claim.Type == "name" && claim.Value == "creator");
        jwt.Claims.Should().Contain(claim => claim.Type == "video" && claim.Value.Contains(token.RoomName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetDeafenedAsync_RequestingUserIsNotAnActiveParticipant_ReturnsNull()
    {
        using var context = TestDbContextFactory.Create();
        var creator = MockData.CreateTestUser("creator", "creator@example.com", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var outsider = MockData.CreateTestUser("outsider", "outsider@example.com", Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var conversation = await SeedConversationAsync(context, creator);
        context.Users.Add(outsider);
        await context.SaveChangesAsync();

        var adminClient = new FakeLiveKitRoomAdminClient(isEnabled: true);
        var service = CreateService(context, ConfiguredOptions(), adminClient);

        var result = await service.SetDeafenedAsync(conversation.Id, outsider.Id, true);

        result.Should().BeNull();
        adminClient.LastSetDeafenedCall.Should().BeNull();
    }

    [Fact]
    public async Task SetDeafenedAsync_ActiveParticipant_DelegatesToRoomAdminClient()
    {
        using var context = TestDbContextFactory.Create();
        var creator = MockData.CreateTestUser("creator", "creator@example.com", Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var conversation = await SeedConversationAsync(context, creator);
        var adminClient = new FakeLiveKitRoomAdminClient(isEnabled: true, setDeafenedResult: true);
        var service = CreateService(context, ConfiguredOptions(), adminClient);

        var result = await service.SetDeafenedAsync(conversation.Id, creator.Id, true);

        result.Should().BeTrue();
        adminClient.LastSetDeafenedCall.Should().NotBeNull();
        var lastSetDeafenedCall = adminClient.LastSetDeafenedCall!.Value;
        lastSetDeafenedCall.RoomName.Should().Be(ConversationVoiceService.GetRoomName(conversation.Id));
        lastSetDeafenedCall.ParticipantIdentity.Should().Be(creator.Id.ToString());
        lastSetDeafenedCall.Deafened.Should().BeTrue();
    }

    private static Conversation CreateConversation(Nuuru.Server.Models.ApplicationUser creator)
    {
        return new Conversation
        {
            Id = Guid.NewGuid(),
            CreatorId = creator.Id,
            Creator = creator,
            Title = "Voice Test Conversation"
        };
    }

    private static async Task<Conversation> SeedConversationAsync(ApplicationDbContext context, Nuuru.Server.Models.ApplicationUser creator)
    {
        var conversation = CreateConversation(creator);
        context.Users.Add(creator);
        context.Conversations.Add(conversation);
        context.ConversationParticipants.Add(new ConversationParticipant
        {
            ConversationId = conversation.Id,
            UserId = creator.Id,
            JoinedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        await context.SaveChangesAsync();
        return conversation;
    }

    private static ConversationVoiceService CreateService(
        ApplicationDbContext context,
        LiveKitOptions options,
        ILiveKitRoomAdminClient roomAdminClient)
    {
        return new ConversationVoiceService(
            context,
            Options.Create(options),
            roomAdminClient);
    }

    private static LiveKitOptions ConfiguredOptions()
    {
        return new LiveKitOptions
        {
            ServerUrl = "wss://livekit.example.com",
            ApiKey = "test-api-key",
            ApiSecret = "1234567890abcdef1234567890abcdef"
        };
    }

    private sealed class FakeLiveKitRoomAdminClient : ILiveKitRoomAdminClient
    {
        private readonly IReadOnlyList<LiveKitRoomParticipant> _participants;
        private readonly bool _setDeafenedResult;

        public FakeLiveKitRoomAdminClient(bool isEnabled, IReadOnlyList<LiveKitRoomParticipant>? participants = null, bool setDeafenedResult = true)
        {
            IsEnabled = isEnabled;
            _participants = participants ?? [];
            _setDeafenedResult = setDeafenedResult;
        }

        public bool IsEnabled { get; }
        public (string RoomName, string ParticipantIdentity, bool Deafened)? LastSetDeafenedCall { get; private set; }

        public Task<IReadOnlyList<LiveKitRoomParticipant>> ListParticipantsAsync(string roomName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_participants);
        }

        public Task<bool> SetParticipantDeafenedAsync(string roomName, string participantIdentity, bool deafened, CancellationToken cancellationToken = default)
        {
            LastSetDeafenedCall = (roomName, participantIdentity, deafened);
            return Task.FromResult(_setDeafenedResult);
        }

        public Task RemoveParticipantAsync(string roomName, string participantIdentity, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}

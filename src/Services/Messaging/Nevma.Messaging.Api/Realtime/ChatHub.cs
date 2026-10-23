using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Nevma.Messaging.Api.Application.Conversations;
using Nevma.Messaging.Api.Application.Presence;

namespace Nevma.Messaging.Api.Realtime;

[Authorize]
public sealed class ChatHub(
    ConversationService conversationService,
    IConversationRepository conversationRepository,
    IUserPresenceTracker presenceTracker) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (!TryGetUserId(out var userId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(userId));
        await presenceTracker.ConnectedAsync(userId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetUserId(out var userId))
            await presenceTracker.DisconnectedAsync(userId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinConversation(Guid conversationId)
    {
        var userId = GetUserId();
        if (!await conversationService.CanAccessAsync(conversationId, userId, Context.ConnectionAborted))
            throw new HubException("Conversation is unavailable.");
        await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroupName(conversationId));
    }

    public async Task LeaveConversation(Guid conversationId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ConversationGroupName(conversationId));

    public async Task Typing(Guid conversationId)
    {
        var userId = GetUserId();
        if (!await conversationService.CanAccessAsync(conversationId, userId, Context.ConnectionAborted))
            throw new HubException("Conversation is unavailable.");
        var recipients = (await conversationRepository.ListParticipantIdsAsync(
                conversationId,
                Context.ConnectionAborted))
            .Where(participantId => participantId != userId)
            .Select(UserGroupName)
            .ToArray();
        if (recipients.Length > 0)
            await Clients.Groups(recipients)
                .SendAsync("typing.changed", new { userId, conversationId }, Context.ConnectionAborted);
    }

    public async Task Heartbeat()
    {
        var userId = GetUserId();
        await presenceTracker.RefreshAsync(userId);
    }

    public static string ConversationGroupName(Guid conversationId) =>
        $"conversation:{conversationId:N}";

    public static string UserGroupName(Guid userId) => $"user:{userId:N}";

    private Guid GetUserId() =>
        TryGetUserId(out var userId)
            ? userId
            : throw new HubException("Authentication is required.");

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(Context.User?.FindFirstValue("sub"), out userId);
}

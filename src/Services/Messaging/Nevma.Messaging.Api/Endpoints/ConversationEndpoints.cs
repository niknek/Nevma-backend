using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Nevma.Contracts.Messaging;
using Nevma.Messaging.Api.Application.Conversations;
using Nevma.Messaging.Api.Application.Messages;
using Nevma.Messaging.Api.Application.Presence;
using Nevma.Messaging.Api.Realtime;

namespace Nevma.Messaging.Api.Endpoints;

public static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var conversations = endpoints.MapGroup("/api/conversations")
            .RequireAuthorization()
            .WithTags("Conversations");

        conversations.MapPost("/", async (
            CreateConversationRequest request,
            ClaimsPrincipal principal,
            ConversationService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var creatorId))
                return Results.Unauthorized();

            var result = await service.CreateAsync(creatorId, request, cancellationToken);
            if (!result.IsSuccess)
                return Results.ValidationProblem(result.Errors);
            return result.IsNew
                ? Results.Created($"/api/conversations/{result.Conversation!.Id}", result.Conversation)
                : Results.Ok(result.Conversation);
        });

        conversations.MapGet("/", async (
            ClaimsPrincipal principal,
            ConversationService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();
            return Results.Ok(await service.ListAsync(userId, cancellationToken));
        });

        conversations.MapPut("/{conversationId:guid}", async (
            Guid conversationId,
            UpdateConversationRequest request,
            ClaimsPrincipal principal,
            ConversationService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var actorId))
                return Results.Unauthorized();
            return MapConversationChange(await service.RenameAsync(
                conversationId,
                actorId,
                request.Title,
                cancellationToken));
        });

        conversations.MapPost("/{conversationId:guid}/participants", async (
            Guid conversationId,
            AddConversationParticipantRequest request,
            ClaimsPrincipal principal,
            ConversationService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var actorId))
                return Results.Unauthorized();
            return MapConversationChange(await service.AddParticipantAsync(
                conversationId,
                actorId,
                request.UserId,
                cancellationToken));
        });

        conversations.MapDelete("/{conversationId:guid}/participants/{userId:guid}", async (
            Guid conversationId,
            Guid userId,
            ClaimsPrincipal principal,
            ConversationService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var actorId))
                return Results.Unauthorized();
            return MapConversationChange(await service.RemoveParticipantAsync(
                conversationId,
                actorId,
                userId,
                cancellationToken));
        });

        conversations.MapGet("/presence/{userId:guid}", async (
            Guid userId,
            ClaimsPrincipal principal,
            IConversationRepository repository,
            IUserPresenceTracker presenceTracker,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var actorId))
                return Results.Unauthorized();
            if (actorId != userId && !await repository.ShareConversationAsync(
                actorId,
                userId,
                cancellationToken))
                return Results.NotFound();
            return Results.Ok(await presenceTracker.GetAsync(userId));
        });

        conversations.MapPost("/presence/heartbeat", async (
            ClaimsPrincipal principal,
            IUserPresenceTracker presenceTracker) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();
            await presenceTracker.RefreshAsync(userId);
            return Results.Ok(await presenceTracker.GetAsync(userId));
        });

        conversations.MapGet("/{conversationId:guid}/messages", async (
            Guid conversationId,
            long? before,
            int? take,
            ClaimsPrincipal principal,
            MessageService service,
            IConversationRepository conversationRepository,
            IHttpClientFactory httpClientFactory,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();

            var page = await service.GetMessagesAsync(
                conversationId,
                userId,
                before,
                take ?? 50,
                cancellationToken);
            return page is null ? Results.NotFound() : Results.Ok(page);
        });

        conversations.MapPost("/{conversationId:guid}/messages", async (
            Guid conversationId,
            SendMessageRequest request,
            HttpRequest httpRequest,
            ClaimsPrincipal principal,
            MessageService service,
            IConversationRepository conversationRepository,
            IHttpClientFactory httpClientFactory,
            IHubContext<ChatHub> hub,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var senderId))
                return Results.Unauthorized();

            var result = await service.SendAsync(
                conversationId,
                senderId,
                request,
                GetAccessToken(httpRequest),
                cancellationToken);
            if (result is SendMessageResult.NotFound)
                return Results.NotFound();
            if (result is SendMessageResult.Invalid invalid)
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { [invalid.Field] = [invalid.Error] });

            var message = ((SendMessageResult.Sent)result).Message;
            await hub.Clients.Group(ChatHub.ConversationGroupName(conversationId))
                .SendAsync("message.received", message, cancellationToken);
            var recipients = await conversationRepository.ListParticipantIdsAsync(conversationId, cancellationToken);
            var notifications = httpClientFactory.CreateClient("Notifications");
            foreach (var recipientId in recipients.Where(id => id != senderId))
            {
                try
                {
                    await notifications.PostAsJsonAsync("/api/notifications/internal/chat",
                        new { userId = recipientId, conversationId, senderName = "Μια επαφή" }, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The durable chat message must succeed even if push delivery is temporarily unavailable.
                }
            }
            return Results.Created(
                $"/api/conversations/{conversationId}/messages/{message.Id}",
                message);
        });

        conversations.MapPut("/{conversationId:guid}/messages/{messageId:guid}", async (
            Guid conversationId,
            Guid messageId,
            EditMessageRequest request,
            ClaimsPrincipal principal,
            MessageService service,
            IHubContext<ChatHub> hub,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var actorId))
                return Results.Unauthorized();
            var result = await service.EditAsync(
                conversationId,
                messageId,
                actorId,
                request,
                cancellationToken);
            if (result is not MessageChangeResult.Changed changed)
                return MapMessageChangeFailure(result);
            await hub.Clients.Group(ChatHub.ConversationGroupName(conversationId))
                .SendAsync("message.updated", changed.Message, cancellationToken);
            return Results.Ok(changed.Message);
        });

        conversations.MapDelete("/{conversationId:guid}/messages/{messageId:guid}", async (
            Guid conversationId,
            Guid messageId,
            ClaimsPrincipal principal,
            MessageService service,
            IHubContext<ChatHub> hub,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var actorId))
                return Results.Unauthorized();
            var result = await service.DeleteAsync(
                conversationId,
                messageId,
                actorId,
                cancellationToken);
            if (result is not MessageChangeResult.Changed changed)
                return MapMessageChangeFailure(result);
            await hub.Clients.Group(ChatHub.ConversationGroupName(conversationId))
                .SendAsync("message.deleted", changed.Message, cancellationToken);
            return Results.Ok(changed.Message);
        });

        conversations.MapPost("/{conversationId:guid}/messages/{messageId:guid}/receipts", async (
            Guid conversationId,
            Guid messageId,
            MessageReceiptRequest request,
            ClaimsPrincipal principal,
            MessageService service,
            IHubContext<ChatHub> hub,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var actorId))
                return Results.Unauthorized();
            var result = await service.MarkReceiptAsync(
                conversationId,
                messageId,
                actorId,
                request.Kind,
                cancellationToken);
            if (result is MessageReceiptResult.NotFound)
                return Results.NotFound();
            if (result is MessageReceiptResult.Invalid invalid)
                return Results.BadRequest(new { message = invalid.Message });
            var receipt = ((MessageReceiptResult.Marked)result).Receipt;
            await hub.Clients.Group(ChatHub.ConversationGroupName(conversationId))
                .SendAsync("message.receipt", receipt, cancellationToken);
            return Results.Ok(receipt);
        });

        conversations.MapPut("/{conversationId:guid}/messages/{messageId:guid}/reaction", async (
            Guid conversationId,
            Guid messageId,
            MessageReactionRequest request,
            ClaimsPrincipal principal,
            MessageService service,
            IHubContext<ChatHub> hub,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var actorId))
                return Results.Unauthorized();
            var result = await service.SetReactionAsync(
                conversationId,
                messageId,
                actorId,
                request.Emoji,
                cancellationToken);
            if (result is MessageReactionResult.NotFound)
                return Results.NotFound();
            if (result is MessageReactionResult.Invalid invalid)
                return Results.BadRequest(new { message = invalid.Message });
            var reaction = ((MessageReactionResult.Changed)result).Reaction;
            await hub.Clients.Group(ChatHub.ConversationGroupName(conversationId))
                .SendAsync("message.reaction", reaction, cancellationToken);
            return Results.Ok(reaction);
        });

        conversations.MapDelete("/{conversationId:guid}/messages/{messageId:guid}/reaction", async (
            Guid conversationId,
            Guid messageId,
            ClaimsPrincipal principal,
            MessageService service,
            IHubContext<ChatHub> hub,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var actorId))
                return Results.Unauthorized();
            if (!await service.RemoveReactionAsync(
                conversationId,
                messageId,
                actorId,
                cancellationToken))
                return Results.NotFound();
            await hub.Clients.Group(ChatHub.ConversationGroupName(conversationId))
                .SendAsync("message.reaction.removed", new { messageId, userId = actorId }, cancellationToken);
            return Results.NoContent();
        });

        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out userId);

    private static string GetAccessToken(HttpRequest request)
    {
        const string prefix = "Bearer ";
        var value = request.Headers.Authorization.ToString();
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..].Trim()
            : string.Empty;
    }

    private static IResult MapMessageChangeFailure(MessageChangeResult result) =>
        result switch
        {
            MessageChangeResult.NotFound => Results.NotFound(),
            MessageChangeResult.Forbidden => Results.Forbid(),
            MessageChangeResult.Invalid invalid => Results.BadRequest(new { message = invalid.Message }),
            MessageChangeResult.Conflict conflict => Results.Conflict(new { message = conflict.Message }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };

    private static IResult MapConversationChange(ConversationChangeResult result) =>
        result switch
        {
            ConversationChangeResult.Changed changed => Results.Ok(changed.Conversation),
            ConversationChangeResult.NotFound => Results.NotFound(),
            ConversationChangeResult.Forbidden => Results.Forbid(),
            ConversationChangeResult.Invalid invalid => Results.BadRequest(new { message = invalid.Message }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
}

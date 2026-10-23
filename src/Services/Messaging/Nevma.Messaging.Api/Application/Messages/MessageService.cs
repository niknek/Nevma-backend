using Nevma.Contracts.Messaging;
using Nevma.Messaging.Api.Application.Conversations;
using Nevma.Messaging.Api.Domain.Messages;

namespace Nevma.Messaging.Api.Application.Messages;

public sealed class MessageService(
    IMessageRepository messageRepository,
    IConversationRepository conversationRepository,
    IMessagingUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IFileAttachmentAuthorizer? fileAttachmentAuthorizer = null)
{
    public async Task<MessagePageResponse?> GetMessagesAsync(
        Guid conversationId,
        Guid userId,
        long? beforeSequence,
        int requestedSize,
        CancellationToken cancellationToken = default)
    {
        if (!await conversationRepository.IsParticipantAsync(conversationId, userId, cancellationToken))
            return null;

        var pageSize = Math.Clamp(requestedSize, 1, 100);
        var messages = await messageRepository.ListAsync(
            conversationId,
            beforeSequence,
            pageSize + 1,
            cancellationToken);
        var hasMore = messages.Count > pageSize;
        var visibleMessages = messages.Take(pageSize).ToArray();
        await MarkReceiptsAsync(visibleMessages, userId, markRead: false, cancellationToken);
        var items = visibleMessages.Select(ToResponse).ToArray();
        var receipts = await messageRepository.ListReceiptsAsync(items.Select(item => item.Id).ToArray(), cancellationToken);
        return new MessagePageResponse(
            items,
            hasMore ? items[^1].Sequence : null,
            receipts.Select(receipt => new MessageReceiptResponse(receipt.MessageId, receipt.UserId, receipt.DeliveredAt, receipt.ReadAt)).ToArray());
    }

    public async Task<SendMessageResult> SendAsync(
        Guid conversationId,
        Guid senderId,
        SendMessageRequest request,
        CancellationToken cancellationToken = default)
        => await SendAsync(conversationId, senderId, request, string.Empty, cancellationToken);

    public async Task<SendMessageResult> SendAsync(
        Guid conversationId,
        Guid senderId,
        SendMessageRequest request,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var text = request.Text ?? string.Empty;
        var attachmentIds = (request.AttachmentIds ?? []).Distinct().ToArray();
        if (string.IsNullOrWhiteSpace(text) && attachmentIds.Length == 0)
            return new SendMessageResult.Invalid("text", "Message text or an attachment is required.");
        if (text.Length > 4_000)
            return new SendMessageResult.Invalid("text", "Message text cannot exceed 4000 characters.");
        if (attachmentIds.Length > 10 || attachmentIds.Any(id => id == Guid.Empty))
            return new SendMessageResult.Invalid("attachmentIds", "A message can contain up to 10 valid attachments.");
        if (!await conversationRepository.IsParticipantAsync(conversationId, senderId, cancellationToken))
            return new SendMessageResult.NotFound();

        // Sending a reply is an unambiguous signal that the sender has opened the
        // conversation and read the messages that were already visible to them.
        var priorMessages = await messageRepository.ListAsync(conversationId, null, 100, cancellationToken);
        await MarkReceiptsAsync(priorMessages, senderId, markRead: true, cancellationToken);

        if (attachmentIds.Length > 0)
        {
            var participants = await conversationRepository.ListParticipantIdsAsync(conversationId, cancellationToken);
            if (fileAttachmentAuthorizer is null || !await fileAttachmentAuthorizer.AuthorizeAsync(
                attachmentIds, senderId, participants, accessToken, cancellationToken))
                return new SendMessageResult.Invalid("attachmentIds", "One or more attachments are unavailable.");
        }

        if (request.ReplyToMessageId is not null)
        {
            var repliedTo = await messageRepository.GetAsync(request.ReplyToMessageId.Value, cancellationToken);
            if (repliedTo is null || repliedTo.ConversationId != conversationId || repliedTo.DeletedAt is not null)
                return new SendMessageResult.Invalid("replyToMessageId", "Reply target is unavailable.");
        }

        var message = Message.Create(
            conversationId,
            senderId,
            text,
            request.ReplyToMessageId,
            attachmentIds,
            timeProvider.GetUtcNow());
        await messageRepository.AddAsync(message, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new SendMessageResult.Sent(ToResponse(message));
    }

    private async Task MarkReceiptsAsync(
        IReadOnlyCollection<Message> messages,
        Guid actorId,
        bool markRead,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var changed = false;
        foreach (var message in messages.Where(item => item.SenderId != actorId))
        {
            var receipt = await messageRepository.GetReceiptAsync(message.Id, actorId, cancellationToken);
            if (receipt is null)
            {
                receipt = MessageReceipt.Create(message.Id, actorId);
                await messageRepository.AddReceiptAsync(receipt, cancellationToken);
            }

            if (markRead)
                receipt.MarkRead(now);
            else
                receipt.MarkDelivered(now);
            changed = true;
        }

        if (changed)
            await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<MessageChangeResult> EditAsync(
        Guid conversationId,
        Guid messageId,
        Guid actorId,
        EditMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 4_000)
            return new MessageChangeResult.Invalid("Message text must contain 1 to 4000 characters.");
        if (!await conversationRepository.IsParticipantAsync(conversationId, actorId, cancellationToken))
            return new MessageChangeResult.NotFound();

        var message = await messageRepository.GetAsync(messageId, cancellationToken);
        if (message is null || message.ConversationId != conversationId)
            return new MessageChangeResult.NotFound();
        if (message.SenderId != actorId)
            return new MessageChangeResult.Forbidden();
        if (!message.Edit(actorId, request.Text, timeProvider.GetUtcNow(), TimeSpan.FromMinutes(15)))
            return new MessageChangeResult.Conflict("The edit window has expired or the message was deleted.");

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MessageChangeResult.Changed(ToResponse(message));
    }

    public async Task<MessageChangeResult> DeleteAsync(
        Guid conversationId,
        Guid messageId,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        if (!await conversationRepository.IsParticipantAsync(conversationId, actorId, cancellationToken))
            return new MessageChangeResult.NotFound();
        var message = await messageRepository.GetAsync(messageId, cancellationToken);
        if (message is null || message.ConversationId != conversationId)
            return new MessageChangeResult.NotFound();
        if (message.SenderId != actorId)
            return new MessageChangeResult.Forbidden();
        if (!message.Delete(actorId, timeProvider.GetUtcNow(), TimeSpan.FromHours(24)))
            return new MessageChangeResult.Conflict("The delete window has expired or the message was deleted.");

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MessageChangeResult.Changed(ToResponse(message));
    }

    public async Task<MessageReceiptResult> MarkReceiptAsync(
        Guid conversationId,
        Guid messageId,
        Guid actorId,
        MessageReceiptKind kind,
        CancellationToken cancellationToken = default)
    {
        if (!await conversationRepository.IsParticipantAsync(conversationId, actorId, cancellationToken))
            return new MessageReceiptResult.NotFound();
        var message = await messageRepository.GetAsync(messageId, cancellationToken);
        if (message is null || message.ConversationId != conversationId)
            return new MessageReceiptResult.NotFound();
        if (message.SenderId == actorId)
            return new MessageReceiptResult.Invalid("A sender cannot acknowledge their own message.");

        var receipt = await messageRepository.GetReceiptAsync(messageId, actorId, cancellationToken);
        if (receipt is null)
        {
            receipt = MessageReceipt.Create(messageId, actorId);
            await messageRepository.AddReceiptAsync(receipt, cancellationToken);
        }
        var now = timeProvider.GetUtcNow();
        if (kind == MessageReceiptKind.Read)
            receipt.MarkRead(now);
        else
            receipt.MarkDelivered(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MessageReceiptResult.Marked(new MessageReceiptResponse(
            receipt.MessageId,
            receipt.UserId,
            receipt.DeliveredAt,
            receipt.ReadAt));
    }

    public async Task<MessageReactionResult> SetReactionAsync(
        Guid conversationId,
        Guid messageId,
        Guid actorId,
        string emoji,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emoji) || emoji.EnumerateRunes().Count() > 8 || emoji.Length > 32)
            return new MessageReactionResult.Invalid("Reaction must contain a short emoji value.");
        if (!await conversationRepository.IsParticipantAsync(conversationId, actorId, cancellationToken))
            return new MessageReactionResult.NotFound();
        var message = await messageRepository.GetAsync(messageId, cancellationToken);
        if (message is null || message.ConversationId != conversationId || message.DeletedAt is not null)
            return new MessageReactionResult.NotFound();

        var now = timeProvider.GetUtcNow();
        var reaction = await messageRepository.GetReactionAsync(messageId, actorId, cancellationToken);
        if (reaction is null)
        {
            reaction = MessageReaction.Create(messageId, actorId, emoji.Trim(), now);
            await messageRepository.AddReactionAsync(reaction, cancellationToken);
        }
        else
        {
            reaction.Change(emoji.Trim(), now);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MessageReactionResult.Changed(new MessageReactionResponse(
            reaction.MessageId,
            reaction.UserId,
            reaction.Emoji,
            reaction.CreatedAt));
    }

    public async Task<bool> RemoveReactionAsync(
        Guid conversationId,
        Guid messageId,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        if (!await conversationRepository.IsParticipantAsync(conversationId, actorId, cancellationToken))
            return false;
        var reaction = await messageRepository.GetReactionAsync(messageId, actorId, cancellationToken);
        if (reaction is null)
            return false;
        messageRepository.RemoveReaction(reaction);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static MessageResponse ToResponse(Message message) =>
        new(
            message.Id,
            message.Sequence,
            message.ConversationId,
            message.SenderId,
            message.Text,
            message.SentAt,
            message.ReplyToMessageId,
            message.EditedAt,
            message.DeletedAt,
            message.Attachments.Select(attachment => attachment.FileAssetId).ToArray());
}

public abstract record MessageChangeResult
{
    public sealed record Changed(MessageResponse Message) : MessageChangeResult;
    public sealed record NotFound : MessageChangeResult;
    public sealed record Forbidden : MessageChangeResult;
    public sealed record Invalid(string Message) : MessageChangeResult;
    public sealed record Conflict(string Message) : MessageChangeResult;
}

public abstract record MessageReceiptResult
{
    public sealed record Marked(MessageReceiptResponse Receipt) : MessageReceiptResult;
    public sealed record NotFound : MessageReceiptResult;
    public sealed record Invalid(string Message) : MessageReceiptResult;
}

public abstract record MessageReactionResult
{
    public sealed record Changed(MessageReactionResponse Reaction) : MessageReactionResult;
    public sealed record NotFound : MessageReactionResult;
    public sealed record Invalid(string Message) : MessageReactionResult;
}

public abstract record SendMessageResult
{
    public sealed record Sent(MessageResponse Message) : SendMessageResult;
    public sealed record NotFound : SendMessageResult;
    public sealed record Invalid(string Field, string Error) : SendMessageResult;
}

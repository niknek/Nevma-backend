namespace Nevma.Contracts.Messaging;

public sealed record CreateConversationRequest(
    ConversationKind Kind,
    string? Title,
    IReadOnlyCollection<Guid> ParticipantIds);

public sealed record ConversationResponse(
    Guid Id,
    ConversationKind Kind,
    string? Title,
    IReadOnlyCollection<Guid> ParticipantIds,
    DateTimeOffset CreatedAt,
    Guid CreatedBy = default);

public sealed record UpdateConversationRequest(string Title);

public sealed record AddConversationParticipantRequest(Guid UserId);

public sealed record PresenceResponse(Guid UserId, bool IsOnline, DateTimeOffset? LastSeenAt);

public sealed record SendMessageRequest(
    string Text,
    Guid? ReplyToMessageId = null,
    IReadOnlyCollection<Guid>? AttachmentIds = null);

public sealed record EditMessageRequest(string Text);

public sealed record MessageReceiptRequest(MessageReceiptKind Kind);

public sealed record MessageReactionRequest(string Emoji);

public sealed record MessageResponse(
    Guid Id,
    long Sequence,
    Guid ConversationId,
    Guid SenderId,
    string Text,
    DateTimeOffset SentAt,
    Guid? ReplyToMessageId = null,
    DateTimeOffset? EditedAt = null,
    DateTimeOffset? DeletedAt = null,
    IReadOnlyCollection<Guid>? AttachmentIds = null);

public sealed record MessageReceiptResponse(
    Guid MessageId,
    Guid UserId,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReadAt);

public sealed record MessageReactionResponse(
    Guid MessageId,
    Guid UserId,
    string Emoji,
    DateTimeOffset CreatedAt);

public sealed record MessagePageResponse(
    IReadOnlyCollection<MessageResponse> Items,
    long? NextBefore,
    IReadOnlyCollection<MessageReceiptResponse>? Receipts = null);

public enum ConversationKind
{
    Personal,
    Group
}

public enum MessageReceiptKind
{
    Delivered,
    Read
}

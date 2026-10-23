using Nevma.Messaging.Api.Domain.Messages;

namespace Nevma.Messaging.Api.Application.Messages;

public interface IMessageRepository
{
    Task AddAsync(Message message, CancellationToken cancellationToken = default);
    Task<Message?> GetAsync(Guid messageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Message>> ListAsync(
        Guid conversationId,
        long? beforeSequence,
        int limit,
        CancellationToken cancellationToken = default);
    Task<MessageReceipt?> GetReceiptAsync(
        Guid messageId,
        Guid userId,
        CancellationToken cancellationToken = default);
    Task AddReceiptAsync(MessageReceipt receipt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MessageReceipt>> ListReceiptsAsync(
        IReadOnlyCollection<Guid> messageIds,
        CancellationToken cancellationToken = default);
    Task<MessageReaction?> GetReactionAsync(
        Guid messageId,
        Guid userId,
        CancellationToken cancellationToken = default);
    Task AddReactionAsync(MessageReaction reaction, CancellationToken cancellationToken = default);
    void RemoveReaction(MessageReaction reaction);
}

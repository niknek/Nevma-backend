using Microsoft.EntityFrameworkCore;
using Nevma.Messaging.Api.Application.Messages;
using Nevma.Messaging.Api.Domain.Messages;
using Nevma.Messaging.Api.Infrastructure.Persistence;

namespace Nevma.Messaging.Api.Infrastructure.Messages;

public sealed class EfMessageRepository(MessagingDbContext dbContext) : IMessageRepository
{
    public async Task AddAsync(Message message, CancellationToken cancellationToken = default) =>
        await dbContext.Messages.AddAsync(message, cancellationToken);

    public Task<Message?> GetAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        dbContext.Messages.Include(message => message.Attachments)
            .SingleOrDefaultAsync(message => message.Id == messageId, cancellationToken);

    public async Task<IReadOnlyList<Message>> ListAsync(
        Guid conversationId,
        long? beforeSequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Messages
            .AsNoTracking()
            .Include(message => message.Attachments)
            .Where(message => message.ConversationId == conversationId);
        if (beforeSequence is not null)
            query = query.Where(message => message.Sequence < beforeSequence);

        return await query
            .OrderByDescending(message => message.Sequence)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<MessageReceipt?> GetReceiptAsync(
        Guid messageId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        dbContext.MessageReceipts.SingleOrDefaultAsync(
            receipt => receipt.MessageId == messageId && receipt.UserId == userId,
            cancellationToken);

    public async Task AddReceiptAsync(
        MessageReceipt receipt,
        CancellationToken cancellationToken = default) =>
        await dbContext.MessageReceipts.AddAsync(receipt, cancellationToken);

    public async Task<IReadOnlyList<MessageReceipt>> ListReceiptsAsync(
        IReadOnlyCollection<Guid> messageIds,
        CancellationToken cancellationToken = default) =>
        await dbContext.MessageReceipts.AsNoTracking()
            .Where(receipt => messageIds.Contains(receipt.MessageId))
            .ToListAsync(cancellationToken);

    public Task<MessageReaction?> GetReactionAsync(
        Guid messageId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        dbContext.MessageReactions.SingleOrDefaultAsync(
            reaction => reaction.MessageId == messageId && reaction.UserId == userId,
            cancellationToken);

    public async Task AddReactionAsync(
        MessageReaction reaction,
        CancellationToken cancellationToken = default) =>
        await dbContext.MessageReactions.AddAsync(reaction, cancellationToken);

    public void RemoveReaction(MessageReaction reaction) => dbContext.MessageReactions.Remove(reaction);
}

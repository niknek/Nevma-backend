using Microsoft.EntityFrameworkCore;
using Nevma.Messaging.Api.Application;
using Nevma.Messaging.Api.Application.Conversations;
using Nevma.Messaging.Api.Application.Integration;
using Nevma.Messaging.Api.Domain.Conversations;
using Nevma.Messaging.Api.Domain.Messages;
using Nevma.Messaging.Api.Domain.Stories;
using Nevma.Messaging.Api.Infrastructure.Inbox;
using Npgsql;

namespace Nevma.Messaging.Api.Infrastructure.Persistence;

public sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options)
    : DbContext(options), IMessagingUnitOfWork
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageReceipt> MessageReceipts => Set<MessageReceipt>();
    public DbSet<MessageReaction> MessageReactions => Set<MessageReaction>();
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<Story> Stories => Set<Story>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("messaging");
        builder.ApplyConfigurationsFromAssembly(typeof(MessagingDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_conversations_PersonalKey"
            })
        {
            throw new DuplicatePersonalConversationException(exception);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "PK_inbox_messages"
            })
        {
            throw new DuplicateInboxEventException(exception);
        }
    }
}

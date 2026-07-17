using System.Data;
using Microsoft.EntityFrameworkCore;
using Nevma.Planning.Api.Application;
using Nevma.Planning.Api.Domain.Calendar;
using Nevma.Planning.Api.Domain.MeetingInvitations;
using Nevma.Planning.Api.Domain.Tasks;
using Nevma.Planning.Api.Infrastructure.Outbox;
using Npgsql;

namespace Nevma.Planning.Api.Infrastructure.Persistence;

public sealed class PlanningDbContext(DbContextOptions<PlanningDbContext> options)
    : DbContext(options), IPlanningUnitOfWork
{
    public DbSet<MeetingInvitation> MeetingInvitations => Set<MeetingInvitation>();
    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
    public DbSet<PersonalCalendarEvent> PersonalCalendarEvents => Set<PersonalCalendarEvent>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskShare> TaskShares => Set<TaskShare>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("planning");
        builder.ApplyConfigurationsFromAssembly(typeof(PlanningDbContext).Assembly);
    }

    public async Task<TResult> ExecuteSerializableAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        if (!Database.IsRelational())
            return await operation(cancellationToken);

        try
        {
            var strategy = Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                ChangeTracker.Clear();
                await using var transaction = await Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                var result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            throw new PlanningConcurrencyException(
                "The calendar changed during conflict detection.",
                exception);
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PlanningConcurrencyException(
                "Planning data was changed by another request.",
                exception);
        }
        catch (DbUpdateException exception) when (
            exception.Entries.Any(entry => entry.Entity is CalendarEvent))
        {
            throw new PlanningConcurrencyException(
                "A calendar event was already created for this invitation.",
                exception);
        }
    }
}

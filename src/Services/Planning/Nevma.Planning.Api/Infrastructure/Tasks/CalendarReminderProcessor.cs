using Microsoft.EntityFrameworkCore;
using Nevma.Contracts.Integration;
using Nevma.Planning.Api.Application;
using Nevma.Planning.Api.Domain.Calendar;
using Nevma.Planning.Api.Infrastructure.Persistence;

namespace Nevma.Planning.Api.Infrastructure.Tasks;

public sealed class CalendarReminderProcessor(
    PlanningDbContext dbContext,
    IPlanningEventOutbox outbox,
    TimeProvider timeProvider)
{
    public async Task<int> ProcessDueAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var expired = await dbContext.PersonalCalendarEvents
            .Where(item => item.Status == PersonalCalendarEventStatus.Pending && item.StartsAt <= now)
            .OrderBy(item => item.StartsAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        foreach (var item in expired)
        {
            if (!item.CancelIfExpired(now)) continue;
            outbox.Add(new CalendarEventExpiredIntegrationEvent(
                Guid.NewGuid(),
                item.Id,
                new[] { item.UserId }.Concat(item.ParticipantIds).Distinct().ToArray(),
                item.Title,
                item.StartsAt,
                now));
        }

        var reminderWindowEnd = now.AddHours(1);
        var events = await dbContext.PersonalCalendarEvents
            .Where(item =>
                item.Status == PersonalCalendarEventStatus.Confirmed &&
                item.StartsAt > now &&
                item.StartsAt <= reminderWindowEnd &&
                item.ReminderDispatchedAt == null)
            .OrderBy(item => item.StartsAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var item in events)
        {
            item.MarkReminderDispatched(now);
            outbox.Add(new CalendarEventReminderDueIntegrationEvent(
                Guid.NewGuid(),
                item.Id,
                new[] { item.UserId }.Concat(item.ParticipantIds).Distinct().ToArray(),
                item.Title,
                item.StartsAt,
                now));
        }

        if (events.Count > 0 || expired.Count > 0)
            await dbContext.SaveChangesAsync(cancellationToken);
        return events.Count;
    }
}

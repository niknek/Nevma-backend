using Nevma.Contracts.Planning;

namespace Nevma.Contracts.Integration;

public sealed record MeetingInvitationChangedIntegrationEvent(
    Guid EventId,
    Guid InvitationId,
    Guid OrganizerId,
    Guid InviteeId,
    string Title,
    DateTimeOffset StartsAt,
    TimeSpan Duration,
    string? Location,
    MeetingInvitationStatus Status,
    DateTimeOffset? ProposedStartsAt,
    TimeSpan? ProposedDuration,
    string? ProposedLocation,
    Guid? CalendarEventId,
    DateTimeOffset OccurredAt);

public sealed record TaskReminderDueIntegrationEvent(
    Guid EventId,
    Guid TaskId,
    Guid OwnerId,
    string Title,
    DateTimeOffset? DueAt,
    DateTimeOffset OccurredAt);

public sealed record CalendarEventReminderDueIntegrationEvent(
    Guid EventId,
    Guid CalendarEventId,
    IReadOnlyCollection<Guid> UserIds,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset OccurredAt);

public static class PlanningIntegrationEventTypes
{
    public const string MeetingInvitationChanged = "planning.meeting-invitation.changed.v1";
    public const string TaskReminderDue = "planning.task-reminder.due.v1";
    public const string CalendarEventReminderDue = "planning.calendar-event-reminder.due.v1";
}

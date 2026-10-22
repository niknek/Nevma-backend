namespace Nevma.Planning.Api.Domain.Calendar;

public sealed class PersonalCalendarEvent
{
    private PersonalCalendarEvent() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Purpose { get; private set; }
    public string? Location { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public Guid[] ParticipantIds { get; private set; } = [];
    public Guid[] AcceptedParticipantIds { get; private set; } = [];
    public PersonalCalendarEventStatus Status { get; private set; } = PersonalCalendarEventStatus.Confirmed;
    public DateTimeOffset? ReminderDispatchedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static PersonalCalendarEvent Create(
        Guid userId,
        string title,
        string? purpose,
        string? location,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        IReadOnlyCollection<Guid>? participantIds,
        DateTimeOffset now)
    {
        var item = new PersonalCalendarEvent { Id = Guid.NewGuid(), UserId = userId };
        item.Update(title, purpose, location, startsAt, endsAt, participantIds, now);
        return item;
    }

    public void Update(
        string title,
        string? purpose,
        string? location,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        IReadOnlyCollection<Guid>? participantIds,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) throw new ArgumentException("A title is required.");
        if (endsAt <= startsAt) throw new ArgumentException("The end must be after the start.");
        Title = title.Trim(); Purpose = purpose?.Trim(); Location = location?.Trim(); StartsAt = startsAt.ToUniversalTime(); EndsAt = endsAt.ToUniversalTime();
        ParticipantIds = (participantIds ?? []).Where(id => id != Guid.Empty && id != UserId).Distinct().ToArray();
        AcceptedParticipantIds = AcceptedParticipantIds.Where(ParticipantIds.Contains).ToArray();
        Status = ParticipantIds.Length == 0 || AcceptedParticipantIds.Length == ParticipantIds.Length
            ? PersonalCalendarEventStatus.Confirmed
            : PersonalCalendarEventStatus.Pending;
        UpdatedAt = now.ToUniversalTime();
    }

    public void MarkReminderDispatched(DateTimeOffset dispatchedAt) =>
        ReminderDispatchedAt = dispatchedAt.ToUniversalTime();

    public ParticipantResponseOutcome Accept(Guid participantId, DateTimeOffset now)
    {
        if (!ParticipantIds.Contains(participantId)) return ParticipantResponseOutcome.Forbidden;
        if (Status == PersonalCalendarEventStatus.Declined || AcceptedParticipantIds.Contains(participantId))
            return ParticipantResponseOutcome.AlreadyHandled;
        AcceptedParticipantIds = [.. AcceptedParticipantIds, participantId];
        Status = AcceptedParticipantIds.Length == ParticipantIds.Length
            ? PersonalCalendarEventStatus.Confirmed
            : PersonalCalendarEventStatus.Pending;
        UpdatedAt = now.ToUniversalTime();
        return ParticipantResponseOutcome.Updated;
    }

    public ParticipantResponseOutcome Decline(Guid participantId, DateTimeOffset now)
    {
        if (!ParticipantIds.Contains(participantId)) return ParticipantResponseOutcome.Forbidden;
        if (Status == PersonalCalendarEventStatus.Declined || AcceptedParticipantIds.Contains(participantId))
            return ParticipantResponseOutcome.AlreadyHandled;
        Status = PersonalCalendarEventStatus.Declined;
        UpdatedAt = now.ToUniversalTime();
        return ParticipantResponseOutcome.Updated;
    }
}

public enum PersonalCalendarEventStatus { Pending, Confirmed, Declined }
public enum ParticipantResponseOutcome { Updated, Forbidden, AlreadyHandled }

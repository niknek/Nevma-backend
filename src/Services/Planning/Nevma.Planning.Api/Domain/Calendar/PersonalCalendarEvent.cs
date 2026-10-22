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
    public Guid[] DeclinedParticipantIds { get; private set; } = [];
    public string[] DeclinedParticipantReasons { get; private set; } = [];
    public PersonalCalendarEventStatus Status { get; private set; } = PersonalCalendarEventStatus.Confirmed;
    public string? CancellationReason { get; private set; }
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
        var retainedDeclines = DeclinedParticipantIds
            .Select((id, index) => new { Id = id, Reason = index < DeclinedParticipantReasons.Length ? DeclinedParticipantReasons[index] : string.Empty })
            .Where(item => ParticipantIds.Contains(item.Id))
            .ToArray();
        DeclinedParticipantIds = retainedDeclines.Select(item => item.Id).ToArray();
        DeclinedParticipantReasons = retainedDeclines.Select(item => item.Reason).ToArray();
        RefreshStatus();
        UpdatedAt = now.ToUniversalTime();
    }

    public void MarkReminderDispatched(DateTimeOffset dispatchedAt) =>
        ReminderDispatchedAt = dispatchedAt.ToUniversalTime();

    public bool CancelIfExpired(DateTimeOffset now)
    {
        if (Status != PersonalCalendarEventStatus.Pending || StartsAt > now)
            return false;
        if (AcceptedParticipantIds.Length > 0)
        {
            Status = PersonalCalendarEventStatus.PartiallyAccepted;
            UpdatedAt = now.ToUniversalTime();
            return false;
        }
        Status = PersonalCalendarEventStatus.Cancelled;
        CancellationReason = "The appointment was not confirmed before its start time.";
        UpdatedAt = now.ToUniversalTime();
        return true;
    }

    public ManualCancellationOutcome Cancel(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return ManualCancellationOutcome.InvalidReason;
        if (Status is PersonalCalendarEventStatus.Cancelled or PersonalCalendarEventStatus.Declined)
            return ManualCancellationOutcome.AlreadyHandled;
        if (StartsAt <= now)
            return ManualCancellationOutcome.AppointmentStarted;
        Status = PersonalCalendarEventStatus.Cancelled;
        CancellationReason = reason.Trim();
        UpdatedAt = now.ToUniversalTime();
        return ManualCancellationOutcome.Updated;
    }

    public ParticipantResponseOutcome Accept(Guid participantId, DateTimeOffset now)
    {
        if (!ParticipantIds.Contains(participantId)) return ParticipantResponseOutcome.Forbidden;
        if (Status is PersonalCalendarEventStatus.Declined or PersonalCalendarEventStatus.Cancelled ||
            AcceptedParticipantIds.Contains(participantId) || DeclinedParticipantIds.Contains(participantId))
            return ParticipantResponseOutcome.AlreadyHandled;
        AcceptedParticipantIds = [.. AcceptedParticipantIds, participantId];
        RefreshStatus();
        UpdatedAt = now.ToUniversalTime();
        return ParticipantResponseOutcome.Updated;
    }

    public ParticipantResponseOutcome Decline(Guid participantId, string? reason, DateTimeOffset now)
    {
        if (!ParticipantIds.Contains(participantId)) return ParticipantResponseOutcome.Forbidden;
        if (Status is PersonalCalendarEventStatus.Declined or PersonalCalendarEventStatus.Cancelled ||
            AcceptedParticipantIds.Contains(participantId) || DeclinedParticipantIds.Contains(participantId))
            return ParticipantResponseOutcome.AlreadyHandled;
        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.Length > 500) return ParticipantResponseOutcome.InvalidReason;
        DeclinedParticipantIds = [.. DeclinedParticipantIds, participantId];
        DeclinedParticipantReasons = [.. DeclinedParticipantReasons, normalizedReason];
        RefreshStatus();
        UpdatedAt = now.ToUniversalTime();
        return ParticipantResponseOutcome.Updated;
    }

    private void RefreshStatus()
    {
        if (ParticipantIds.Length == 0 || AcceptedParticipantIds.Length == ParticipantIds.Length)
            Status = PersonalCalendarEventStatus.Confirmed;
        else if (AcceptedParticipantIds.Length + DeclinedParticipantIds.Length == ParticipantIds.Length)
            Status = AcceptedParticipantIds.Length > 0
                ? PersonalCalendarEventStatus.PartiallyAccepted
                : PersonalCalendarEventStatus.Declined;
        else
            Status = PersonalCalendarEventStatus.Pending;
    }
}

public enum PersonalCalendarEventStatus { Pending, Confirmed, Declined, Cancelled, PartiallyAccepted }
public enum ParticipantResponseOutcome { Updated, Forbidden, AlreadyHandled, InvalidReason }
public enum ManualCancellationOutcome { Updated, InvalidReason, AlreadyHandled, AppointmentStarted }

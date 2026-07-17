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
    public DateTimeOffset UpdatedAt { get; private set; }

    public static PersonalCalendarEvent Create(Guid userId, string title, string? purpose, string? location, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset now)
    {
        var item = new PersonalCalendarEvent { Id = Guid.NewGuid(), UserId = userId };
        item.Update(title, purpose, location, startsAt, endsAt, now);
        return item;
    }

    public void Update(string title, string? purpose, string? location, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) throw new ArgumentException("A title is required.");
        if (endsAt <= startsAt) throw new ArgumentException("The end must be after the start.");
        Title = title.Trim(); Purpose = purpose?.Trim(); Location = location?.Trim(); StartsAt = startsAt.ToUniversalTime(); EndsAt = endsAt.ToUniversalTime(); UpdatedAt = now.ToUniversalTime();
    }
}

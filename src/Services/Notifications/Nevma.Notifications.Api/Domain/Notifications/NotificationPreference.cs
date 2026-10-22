namespace Nevma.Notifications.Api.Domain.Notifications;

public sealed class NotificationPreference
{
    private NotificationPreference(
        Guid userId,
        bool pushEnabled,
        bool meetingNotificationsEnabled,
        bool calendarRemindersEnabled,
        bool taskRemindersEnabled,
        TimeOnly? quietHoursStart,
        TimeOnly? quietHoursEnd,
        string timeZoneId,
        DateTimeOffset updatedAt)
    {
        UserId = userId;
        PushEnabled = pushEnabled;
        MeetingNotificationsEnabled = meetingNotificationsEnabled;
        CalendarRemindersEnabled = calendarRemindersEnabled;
        TaskRemindersEnabled = taskRemindersEnabled;
        QuietHoursStart = quietHoursStart;
        QuietHoursEnd = quietHoursEnd;
        TimeZoneId = timeZoneId;
        UpdatedAt = updatedAt;
    }

    public Guid UserId { get; }
    public bool PushEnabled { get; private set; }
    public bool MeetingNotificationsEnabled { get; private set; }
    public bool CalendarRemindersEnabled { get; private set; }
    public bool TaskRemindersEnabled { get; private set; }
    public TimeOnly? QuietHoursStart { get; private set; }
    public TimeOnly? QuietHoursEnd { get; private set; }
    public string TimeZoneId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static NotificationPreference CreateDefault(Guid userId, DateTimeOffset now) =>
        new(userId, true, true, true, true, null, null, "UTC", now);

    public void Update(
        bool pushEnabled,
        bool meetingNotificationsEnabled,
        bool calendarRemindersEnabled,
        bool taskRemindersEnabled,
        TimeOnly? quietHoursStart,
        TimeOnly? quietHoursEnd,
        string timeZoneId,
        DateTimeOffset now)
    {
        PushEnabled = pushEnabled;
        MeetingNotificationsEnabled = meetingNotificationsEnabled;
        CalendarRemindersEnabled = calendarRemindersEnabled;
        TaskRemindersEnabled = taskRemindersEnabled;
        QuietHoursStart = quietHoursStart;
        QuietHoursEnd = quietHoursEnd;
        TimeZoneId = timeZoneId.Trim();
        UpdatedAt = now;
    }

    public DeliveryPreference Evaluate(string notificationType, DateTimeOffset now)
    {
        if (!PushEnabled ||
            (notificationType.StartsWith("meeting-", StringComparison.Ordinal) && !MeetingNotificationsEnabled) ||
            (notificationType == "calendar.reminder" && !CalendarRemindersEnabled) ||
            (notificationType == "task.reminder" && !TaskRemindersEnabled))
            return DeliveryPreference.Disabled;
        if (QuietHoursStart is null || QuietHoursEnd is null || QuietHoursStart == QuietHoursEnd)
            return DeliveryPreference.Allowed;

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localTime = TimeOnly.FromDateTime(localNow.DateTime);
        var spansMidnight = QuietHoursStart > QuietHoursEnd;
        var isQuiet = spansMidnight
            ? localTime >= QuietHoursStart || localTime < QuietHoursEnd
            : localTime >= QuietHoursStart && localTime < QuietHoursEnd;
        if (!isQuiet)
            return DeliveryPreference.Allowed;

        var endDate = spansMidnight && localTime >= QuietHoursStart
            ? localNow.Date.AddDays(1)
            : localNow.Date;
        var localEnd = endDate + QuietHoursEnd.Value.ToTimeSpan();
        var utcEnd = TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone);
        return new DeliveryPreference.Deferred(new DateTimeOffset(utcEnd, TimeSpan.Zero));
    }
}

public abstract record DeliveryPreference
{
    public sealed record Allow : DeliveryPreference;
    public sealed record Disable : DeliveryPreference;
    public sealed record Deferred(DateTimeOffset Until) : DeliveryPreference;

    public static DeliveryPreference Allowed { get; } = new Allow();
    public static DeliveryPreference Disabled { get; } = new Disable();
}

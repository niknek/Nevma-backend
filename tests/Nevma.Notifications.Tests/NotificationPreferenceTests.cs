using Nevma.Notifications.Api.Domain.Notifications;

namespace Nevma.Notifications.Tests;

public sealed class NotificationPreferenceTests
{
    [Fact]
    public void Quiet_hours_defer_push_until_the_local_end_time()
    {
        var now = new DateTimeOffset(2026, 7, 15, 22, 30, 0, TimeSpan.Zero);
        var preference = NotificationPreference.CreateDefault(Guid.NewGuid(), now);
        preference.Update(true, true, true, true, new TimeOnly(22, 0), new TimeOnly(7, 0), "UTC", now);

        var result = Assert.IsType<DeliveryPreference.Deferred>(
            preference.Evaluate("task.reminder", now));

        Assert.Equal(new DateTimeOffset(2026, 7, 16, 7, 0, 0, TimeSpan.Zero), result.Until);
    }

    [Fact]
    public void Disabled_task_reminders_are_not_delivered()
    {
        var now = DateTimeOffset.UtcNow;
        var preference = NotificationPreference.CreateDefault(Guid.NewGuid(), now);
        preference.Update(true, true, true, false, null, null, "UTC", now);

        Assert.IsType<DeliveryPreference.Disable>(preference.Evaluate("task.reminder", now));
    }

    [Fact]
    public void Disabled_calendar_reminders_do_not_disable_meeting_notifications()
    {
        var now = DateTimeOffset.UtcNow;
        var preference = NotificationPreference.CreateDefault(Guid.NewGuid(), now);
        preference.Update(true, true, false, true, null, null, "UTC", now);

        Assert.IsType<DeliveryPreference.Disable>(preference.Evaluate("calendar.reminder", now));
        Assert.IsType<DeliveryPreference.Allow>(preference.Evaluate("meeting-invitation.created", now));
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nevma.Notifications.Api.Domain.Notifications;

namespace Nevma.Notifications.Api.Infrastructure.Persistence.Configurations;

public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences", "notifications");
        builder.HasKey(preference => preference.UserId);
        builder.Property(preference => preference.PushEnabled).IsRequired();
        builder.Property(preference => preference.MeetingNotificationsEnabled).IsRequired();
        builder.Property(preference => preference.CalendarRemindersEnabled).IsRequired();
        builder.Property(preference => preference.TaskRemindersEnabled).IsRequired();
        builder.Property(preference => preference.QuietHoursStart);
        builder.Property(preference => preference.QuietHoursEnd);
        builder.Property(preference => preference.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(preference => preference.UpdatedAt).IsRequired();
    }
}

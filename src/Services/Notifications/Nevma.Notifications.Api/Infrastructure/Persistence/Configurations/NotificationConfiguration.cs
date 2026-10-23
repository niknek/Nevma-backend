using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nevma.Notifications.Api.Domain.Notifications;

namespace Nevma.Notifications.Api.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", "notifications");
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).ValueGeneratedNever();
        builder.Property(notification => notification.UserId).IsRequired();
        builder.Property(notification => notification.Type).HasMaxLength(100).IsRequired();
        builder.Property(notification => notification.Title).HasMaxLength(200).IsRequired();
        builder.Property(notification => notification.Body).HasMaxLength(4_000).IsRequired();
        builder.Property(notification => notification.CreatedAt).IsRequired();
        builder.Property(notification => notification.ReadAt);
        builder.Property(notification => notification.ResourceId);
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasIndex(notification => new
        {
            notification.UserId,
            notification.ReadAt,
            notification.CreatedAt
        });
    }
}

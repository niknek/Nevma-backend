using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nevma.Planning.Api.Domain.Calendar;

namespace Nevma.Planning.Api.Infrastructure.Persistence.Configurations;

public sealed class PersonalCalendarEventConfiguration : IEntityTypeConfiguration<PersonalCalendarEvent>
{
    public void Configure(EntityTypeBuilder<PersonalCalendarEvent> builder)
    {
        builder.ToTable("personal_calendar_events", "planning");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Purpose).HasMaxLength(1000);
        builder.Property(x => x.Location).HasMaxLength(500);
        builder.Property(x => x.CancellationReason).HasMaxLength(500);
        builder.Property(x => x.ParticipantIds).HasColumnType("uuid[]");
        builder.Property(x => x.AcceptedParticipantIds).HasColumnType("uuid[]");
        builder.Property(x => x.DeclinedParticipantIds).HasColumnType("uuid[]");
        builder.Property(x => x.DeclinedParticipantReasons).HasColumnType("text[]");
        builder.Property(x => x.LiveLocationSharingUserIds).HasColumnType("uuid[]");
        builder.Property(x => x.ReminderDispatchedAt);
        builder.HasIndex(x => new { x.Status, x.StartsAt, x.ReminderDispatchedAt });
        builder.HasIndex(x => new { x.UserId, x.StartsAt });
        builder.HasIndex(x => x.ParticipantIds).HasMethod("gin");
    }
}

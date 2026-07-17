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
        builder.HasIndex(x => new { x.UserId, x.StartsAt });
    }
}

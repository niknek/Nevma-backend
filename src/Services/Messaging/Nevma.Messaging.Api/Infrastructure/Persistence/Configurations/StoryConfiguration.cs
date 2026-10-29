using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nevma.Messaging.Api.Domain.Stories;

namespace Nevma.Messaging.Api.Infrastructure.Persistence.Configurations;

public sealed class StoryConfiguration : IEntityTypeConfiguration<Story>
{
    public void Configure(EntityTypeBuilder<Story> builder)
    {
        builder.ToTable("stories", "messaging");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.MediaType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Caption).HasMaxLength(500);
        builder.Property(x => x.AudienceIds).HasColumnType("uuid[]");
        builder.Property(x => x.SeenByIds).HasColumnType("uuid[]");
        builder.Property(x => x.LikedByIds).HasColumnType("uuid[]");
        builder.HasIndex(x => x.OwnerId);
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasIndex(x => x.AudienceIds).HasMethod("gin");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nevma.Identity.Api.Domain.Users;

namespace Nevma.Identity.Api.Infrastructure.Persistence.Configurations;

public sealed class StoryAudienceGroupConfiguration : IEntityTypeConfiguration<StoryAudienceGroup>
{
    public void Configure(EntityTypeBuilder<StoryAudienceGroup> builder)
    {
        builder.ToTable("story_audience_groups", "identity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(80).IsRequired();
        builder.Property(x => x.MemberIds).HasColumnType("uuid[]").IsRequired();
        builder.HasIndex(x => x.OwnerId);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}

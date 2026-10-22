using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nevma.Identity.Api.Domain.Users;
using Nevma.Identity.Api.Infrastructure.Persistence;

namespace Nevma.Identity.Api.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", "identity");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(user => user.AvatarUrl).HasMaxLength(2_048);
        builder.Property(user => user.Bio).HasMaxLength(300);
        builder.Property(user => user.ContactEmail).HasMaxLength(320);
        builder.Property(user => user.Phone).HasMaxLength(30);
        builder.Property(user => user.Availability).HasMaxLength(20).HasDefaultValue("Available").IsRequired();
        builder.Property(user => user.StoryPrivacy).HasMaxLength(20).HasDefaultValue("Contacts").IsRequired();
        builder.Property(user => user.CreatedAt).IsRequired();
        builder
            .HasOne<IdentityAccount>()
            .WithOne()
            .HasForeignKey<User>(user => user.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nevma.Identity.Api.Domain.Users;
using Nevma.Identity.Api.Domain.Connections;
using Nevma.Identity.Api.Application;
using Nevma.Identity.Api.Domain.Sessions;
using Nevma.Identity.Api.Domain.Security;
using Nevma.Identity.Api.Domain.AddressBook;
using OpenIddict.EntityFrameworkCore;

namespace Nevma.Identity.Api.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityDbContext<IdentityAccount, IdentityRole<Guid>, Guid>(options), IIdentityUnitOfWork
{
    public DbSet<User> Profiles => Set<User>();
    public DbSet<Nevma.Identity.Api.Infrastructure.AddressBook.ContactJoinedNotice> ContactJoinedNotices => Set<Nevma.Identity.Api.Infrastructure.AddressBook.ContactJoinedNotice>();
    public DbSet<StoryAudienceGroup> StoryAudienceGroups => Set<StoryAudienceGroup>();
    public DbSet<ContactConnection> ContactConnections => Set<ContactConnection>();
    public DbSet<AddressBookContact> AddressBookContacts => Set<AddressBookContact>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<BlockedUser> BlockedUsers => Set<BlockedUser>();
    public DbSet<UserReport> UserReports => Set<UserReport>();
    public DbSet<DeviceSession> DeviceSessions => Set<DeviceSession>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("identity");
        builder.Entity<Nevma.Identity.Api.Infrastructure.AddressBook.ContactJoinedNotice>(entity =>
        {
            entity.ToTable("contact_joined_notices");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.DisplayName).HasMaxLength(150);
            entity.HasIndex(item => new { item.OwnerId, item.JoinedUserId }).IsUnique();
        });
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
        builder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}

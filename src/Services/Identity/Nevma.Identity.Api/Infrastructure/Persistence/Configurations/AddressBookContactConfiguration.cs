using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nevma.Identity.Api.Domain.AddressBook;

namespace Nevma.Identity.Api.Infrastructure.Persistence.Configurations;

public sealed class AddressBookContactConfiguration : IEntityTypeConfiguration<AddressBookContact>
{
    public void Configure(EntityTypeBuilder<AddressBookContact> builder)
    {
        builder.ToTable("address_book_contacts", "identity");
        builder.HasKey(contact => contact.Id);
        builder.Property(contact => contact.Name).HasMaxLength(150).IsRequired();
        builder.Property(contact => contact.Phone).HasMaxLength(20).IsRequired();
        builder.Property(contact => contact.Email).HasMaxLength(320);
        builder.Property(contact => contact.Social).HasMaxLength(500);
        builder.Property(contact => contact.Address).HasMaxLength(500);
        builder.Property(contact => contact.Notes).HasMaxLength(1_000);
        builder.HasIndex(contact => new { contact.OwnerId, contact.Phone }).IsUnique();
        builder.HasIndex(contact => new { contact.OwnerId, contact.Name });
        builder.HasIndex(contact => contact.LinkedUserId);
    }
}

using Microsoft.EntityFrameworkCore;
using Nevma.Identity.Api.Application.AddressBook;
using Nevma.Identity.Api.Domain.AddressBook;
using Nevma.Identity.Api.Infrastructure.Persistence;

namespace Nevma.Identity.Api.Infrastructure.AddressBook;

public sealed class EfAddressBookRepository(IdentityDbContext db) : IAddressBookRepository
{
    public async Task<IReadOnlyList<AddressBookContact>> ListAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        await db.AddressBookContacts.AsNoTracking()
            .Where(contact => contact.OwnerId == ownerId)
            .OrderBy(contact => contact.Name)
            .ToListAsync(cancellationToken);

    public Task<AddressBookContact?> GetAsync(
        Guid ownerId,
        Guid id,
        CancellationToken cancellationToken = default) =>
        db.AddressBookContacts.SingleOrDefaultAsync(
            contact => contact.OwnerId == ownerId && contact.Id == id,
            cancellationToken);

    public Task<bool> PhoneExistsAsync(
        Guid ownerId,
        string phone,
        Guid? exceptId = null,
        CancellationToken cancellationToken = default) =>
        db.AddressBookContacts.AnyAsync(
            contact => contact.OwnerId == ownerId && contact.Phone == phone &&
                       (exceptId == null || contact.Id != exceptId),
            cancellationToken);

    public Task<Guid?> ResolveLinkedUserIdAsync(
        Guid ownerId,
        string phone,
        string? email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToUpperInvariant();
        return db.Users
            .Where(account => account.Id != ownerId &&
                (account.PhoneNumber == phone ||
                 (normalizedEmail != null && account.NormalizedEmail == normalizedEmail)))
            .Select(account => (Guid?)account.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(AddressBookContact contact, CancellationToken cancellationToken = default) =>
        await db.AddressBookContacts.AddAsync(contact, cancellationToken);

    public void Remove(AddressBookContact contact) => db.AddressBookContacts.Remove(contact);
}

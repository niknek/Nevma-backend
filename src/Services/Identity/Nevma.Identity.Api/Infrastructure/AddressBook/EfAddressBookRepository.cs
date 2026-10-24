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
        await db.AddressBookContacts
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

    public async Task<Guid?> ResolveLinkedUserIdAsync(
        Guid ownerId,
        string phone,
        string? email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToUpperInvariant();
        if (normalizedEmail is not null)
        {
            var emailMatch = await db.Users
                .Where(account => account.Id != ownerId && account.NormalizedEmail == normalizedEmail)
                .Select(account => (Guid?)account.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (emailMatch is not null) return emailMatch;
        }

        var phoneKey = PhoneMatchKey(phone);
        var profilePhones = await db.Profiles.AsNoTracking()
            .Where(profile => profile.Id != ownerId && profile.Phone != null)
            .Select(profile => new { profile.Id, profile.Phone })
            .ToListAsync(cancellationToken);
        var profileMatch = profilePhones.FirstOrDefault(profile => PhoneMatchKey(profile.Phone!) == phoneKey);
        if (profileMatch is not null) return profileMatch.Id;

        var accountPhones = await db.Users.AsNoTracking()
            .Where(account => account.Id != ownerId && account.PhoneNumber != null)
            .Select(account => new { account.Id, account.PhoneNumber })
            .ToListAsync(cancellationToken);
        return accountPhones
            .Where(account => PhoneMatchKey(account.PhoneNumber!) == phoneKey)
            .Select(account => (Guid?)account.Id)
            .FirstOrDefault();
    }

    private static string PhoneMatchKey(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0030", StringComparison.Ordinal) && digits.Length == 14) return digits[4..];
        if (digits.StartsWith("30", StringComparison.Ordinal) && digits.Length == 12) return digits[2..];
        return digits;
    }

    public async Task AddAsync(AddressBookContact contact, CancellationToken cancellationToken = default) =>
        await db.AddressBookContacts.AddAsync(contact, cancellationToken);

    public void Remove(AddressBookContact contact) => db.AddressBookContacts.Remove(contact);
}

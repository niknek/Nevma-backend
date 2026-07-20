using Nevma.Identity.Api.Domain.AddressBook;

namespace Nevma.Identity.Api.Application.AddressBook;

public interface IAddressBookRepository
{
    Task<IReadOnlyList<AddressBookContact>> ListAsync(Guid ownerId, CancellationToken cancellationToken = default);
    Task<AddressBookContact?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken = default);
    Task<bool> PhoneExistsAsync(Guid ownerId, string phone, Guid? exceptId = null, CancellationToken cancellationToken = default);
    Task<Guid?> ResolveLinkedUserIdAsync(Guid ownerId, string phone, string? email, CancellationToken cancellationToken = default);
    Task AddAsync(AddressBookContact contact, CancellationToken cancellationToken = default);
    void Remove(AddressBookContact contact);
}

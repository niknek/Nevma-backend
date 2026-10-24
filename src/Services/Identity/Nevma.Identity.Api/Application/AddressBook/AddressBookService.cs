using Nevma.Contracts.Identity;
using Nevma.Identity.Api.Domain.AddressBook;

namespace Nevma.Identity.Api.Application.AddressBook;

public sealed class AddressBookService(
    IAddressBookRepository repository,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<AddressBookContactResponse>> ListAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var contacts = await repository.ListAsync(ownerId, cancellationToken);
        var changed = false;
        foreach (var contact in contacts.Where(contact => contact.LinkedUserId is null))
        {
            var linkedUserId = await repository.ResolveLinkedUserIdAsync(
                ownerId, contact.Phone, contact.Email, cancellationToken);
            if (linkedUserId is null) continue;
            contact.Update(contact.Name, contact.Phone, contact.Email, contact.Social, contact.Address,
                contact.Notes, linkedUserId, clock.GetUtcNow());
            changed = true;
        }
        if (changed) await unitOfWork.SaveChangesAsync(cancellationToken);
        return contacts.Select(ToResponse).ToArray();
    }

    public async Task<AddressBookSaveResult> CreateAsync(
        Guid ownerId,
        SaveAddressBookContactRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var phone = AddressBookContact.NormalizePhone(request.Phone);
            if (await repository.PhoneExistsAsync(ownerId, phone, cancellationToken: cancellationToken))
                return new AddressBookSaveResult.DuplicatePhone();

            var linkedUserId = await repository.ResolveLinkedUserIdAsync(ownerId, phone, request.Email, cancellationToken);
            var contact = AddressBookContact.Create(
                ownerId, request.Name, phone, request.Email, request.Social, request.Address, request.Notes,
                linkedUserId, clock.GetUtcNow());
            await repository.AddAsync(contact, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new AddressBookSaveResult.Saved(ToResponse(contact));
        }
        catch (ArgumentException exception)
        {
            return new AddressBookSaveResult.Invalid(exception.Message);
        }
    }

    public async Task<AddressBookSaveResult> UpdateAsync(
        Guid ownerId,
        Guid id,
        SaveAddressBookContactRequest request,
        CancellationToken cancellationToken = default)
    {
        var contact = await repository.GetAsync(ownerId, id, cancellationToken);
        if (contact is null) return new AddressBookSaveResult.NotFound();

        try
        {
            var phone = AddressBookContact.NormalizePhone(request.Phone);
            if (await repository.PhoneExistsAsync(ownerId, phone, id, cancellationToken))
                return new AddressBookSaveResult.DuplicatePhone();

            var linkedUserId = await repository.ResolveLinkedUserIdAsync(ownerId, phone, request.Email, cancellationToken);
            contact.Update(
                request.Name, phone, request.Email, request.Social, request.Address, request.Notes,
                linkedUserId, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new AddressBookSaveResult.Saved(ToResponse(contact));
        }
        catch (ArgumentException exception)
        {
            return new AddressBookSaveResult.Invalid(exception.Message);
        }
    }

    public async Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken = default)
    {
        var contact = await repository.GetAsync(ownerId, id, cancellationToken);
        if (contact is null) return false;
        repository.Remove(contact);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static AddressBookContactResponse ToResponse(AddressBookContact contact) => new(
        contact.Id,
        contact.Name,
        contact.Phone,
        contact.Email,
        contact.Social,
        contact.Address,
        contact.Notes,
        contact.LinkedUserId,
        contact.CreatedAt,
        contact.UpdatedAt);
}

public abstract record AddressBookSaveResult
{
    public sealed record Saved(AddressBookContactResponse Contact) : AddressBookSaveResult;
    public sealed record Invalid(string Message) : AddressBookSaveResult;
    public sealed record DuplicatePhone : AddressBookSaveResult;
    public sealed record NotFound : AddressBookSaveResult;
}

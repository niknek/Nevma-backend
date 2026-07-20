using Microsoft.EntityFrameworkCore;
using Nevma.Contracts.Identity;
using Nevma.Identity.Api.Application.AddressBook;
using Nevma.Identity.Api.Infrastructure.AddressBook;
using Nevma.Identity.Api.Infrastructure.Persistence;

namespace Nevma.Identity.Tests;

public sealed class AddressBookTests
{
    [Fact]
    public async Task Name_and_phone_are_required()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var missingName = await service.CreateAsync(
            Guid.NewGuid(), new SaveAddressBookContactRequest("", "6912345678", null, null, null, null));
        var missingPhone = await service.CreateAsync(
            Guid.NewGuid(), new SaveAddressBookContactRequest("Maria", "", null, null, null, null));
        var invalidEmail = await service.CreateAsync(
            Guid.NewGuid(), new SaveAddressBookContactRequest("Maria", "6912345678", "not-an-email", null, null, null));

        Assert.IsType<AddressBookSaveResult.Invalid>(missingName);
        Assert.IsType<AddressBookSaveResult.Invalid>(missingPhone);
        Assert.IsType<AddressBookSaveResult.Invalid>(invalidEmail);
    }

    [Fact]
    public async Task Owner_can_create_update_list_and_delete_a_contact()
    {
        await using var context = CreateContext();
        var ownerId = Guid.NewGuid();
        var service = CreateService(context);

        var created = Assert.IsType<AddressBookSaveResult.Saved>(await service.CreateAsync(
            ownerId,
            new SaveAddressBookContactRequest(
                "Maria", "+30 691 234 5678", "maria@example.test", "@maria", "Athens", "Dentist")));
        var updated = Assert.IsType<AddressBookSaveResult.Saved>(await service.UpdateAsync(
            ownerId,
            created.Contact.Id,
            new SaveAddressBookContactRequest(
                "Maria P.", "+30 691 234 5678", "maria@example.test", "@maria", "Piraeus", null)));

        var contact = Assert.Single(await service.ListAsync(ownerId));
        Assert.Equal("Maria P.", contact.Name);
        Assert.Equal("+306912345678", contact.Phone);
        Assert.Equal("Piraeus", contact.Address);
        Assert.Null(contact.Notes);
        Assert.Equal(updated.Contact.Id, contact.Id);
        Assert.True(await service.DeleteAsync(ownerId, contact.Id));
        Assert.Empty(await service.ListAsync(ownerId));
    }

    [Fact]
    public async Task Contact_links_to_an_existing_nevma_account_by_email()
    {
        await using var context = CreateContext();
        var account = new IdentityAccount
        {
            Id = Guid.NewGuid(),
            Email = "maria@example.test",
            NormalizedEmail = "MARIA@EXAMPLE.TEST",
            UserName = "maria@example.test",
            NormalizedUserName = "MARIA@EXAMPLE.TEST"
        };
        context.Users.Add(account);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var result = Assert.IsType<AddressBookSaveResult.Saved>(await service.CreateAsync(
            Guid.NewGuid(),
            new SaveAddressBookContactRequest("Maria", "6912345678", "maria@example.test", null, null, null)));

        Assert.Equal(account.Id, result.Contact.LinkedUserId);
    }

    [Fact]
    public async Task Phone_must_be_unique_per_owner()
    {
        await using var context = CreateContext();
        var ownerId = Guid.NewGuid();
        var service = CreateService(context);
        await service.CreateAsync(ownerId, new SaveAddressBookContactRequest("Maria", "6912345678", null, null, null, null));

        var duplicate = await service.CreateAsync(
            ownerId, new SaveAddressBookContactRequest("Nikos", "691 234 5678", null, null, null, null));

        Assert.IsType<AddressBookSaveResult.DuplicatePhone>(duplicate);
    }

    private static IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"address-book-{Guid.NewGuid():N}")
            .Options;
        return new IdentityDbContext(options);
    }

    private static AddressBookService CreateService(IdentityDbContext context) =>
        new(new EfAddressBookRepository(context), context, TimeProvider.System);
}

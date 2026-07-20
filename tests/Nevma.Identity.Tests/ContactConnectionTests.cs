using Microsoft.EntityFrameworkCore;
using Nevma.Contracts.Identity;
using Nevma.Identity.Api.Application.Connections;
using Nevma.Identity.Api.Domain.Connections;
using Nevma.Identity.Api.Domain.Users;
using Nevma.Identity.Api.Infrastructure.Connections;
using Nevma.Identity.Api.Infrastructure.Persistence;
using Nevma.Identity.Api.Infrastructure.Users;
using ContractStatus = Nevma.Contracts.Identity.ContactConnectionStatus;

namespace Nevma.Identity.Tests;

public sealed class ContactConnectionTests
{
    [Fact]
    public void Only_the_addressee_can_accept_a_pending_request()
    {
        var requesterId = Guid.NewGuid();
        var addresseeId = Guid.NewGuid();
        var connection = ContactConnection.Create(requesterId, addresseeId, DateTimeOffset.UtcNow);

        var unauthorized = connection.Accept(requesterId, DateTimeOffset.UtcNow);
        var accepted = connection.Accept(addresseeId, DateTimeOffset.UtcNow);

        Assert.Equal(ConnectionTransition.Forbidden, unauthorized);
        Assert.Equal(ConnectionTransition.Success, accepted);
        Assert.Equal(Nevma.Identity.Api.Domain.Connections.ContactConnectionStatus.Accepted, connection.Status);
    }

    [Fact]
    public async Task Duplicate_pending_request_returns_a_conflict()
    {
        await using var context = CreateContext();
        var (requester, target) = await AddProfilesAsync(context);
        var service = CreateService(context);
        var request = new CreateContactConnectionRequest(target.Id);

        var first = await service.RequestAsync(requester.Id, request);
        var duplicate = await service.RequestAsync(requester.Id, request);

        Assert.True(first.IsSuccess);
        Assert.True(duplicate.IsFailure);
        Assert.Equal(ContactConnectionErrors.AlreadyExists, duplicate.Error);
        Assert.Equal(1, await context.ContactConnections.CountAsync());
    }

    [Fact]
    public async Task Addressee_can_accept_and_requester_cannot()
    {
        await using var context = CreateContext();
        var (requester, target) = await AddProfilesAsync(context);
        var service = CreateService(context);
        var created = await service.RequestAsync(
            requester.Id,
            new CreateContactConnectionRequest(target.Id));

        var forbidden = await service.AcceptAsync(created.Value.Id, requester.Id);
        var accepted = await service.AcceptAsync(created.Value.Id, target.Id);

        Assert.Equal(ContactConnectionErrors.Forbidden, forbidden.Error);
        Assert.True(accepted.IsSuccess);
        Assert.Equal(ContractStatus.Accepted, accepted.Value.Status);
    }

    [Fact]
    public async Task Contact_picker_returns_only_accepted_profiles()
    {
        await using var context = CreateContext();
        var requester = User.Create(Guid.NewGuid(), "Requester", null, DateTimeOffset.UtcNow);
        var acceptedContact = User.Create(Guid.NewGuid(), "Maria Papadopoulou", "https://example.test/maria.png", DateTimeOffset.UtcNow);
        var pendingContact = User.Create(Guid.NewGuid(), "Nikos Georgiou", null, DateTimeOffset.UtcNow);
        context.Profiles.AddRange(requester, acceptedContact, pendingContact);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var acceptedRequest = await service.RequestAsync(requester.Id, new CreateContactConnectionRequest(acceptedContact.Id));
        await service.AcceptAsync(acceptedRequest.Value.Id, acceptedContact.Id);
        await service.RequestAsync(requester.Id, new CreateContactConnectionRequest(pendingContact.Id));

        var contacts = await service.ListContactsAsync(requester.Id);

        var contact = Assert.Single(contacts);
        Assert.Equal(acceptedContact.Id, contact.UserId);
        Assert.Equal("Maria Papadopoulou", contact.DisplayName);
        Assert.Equal("https://example.test/maria.png", contact.AvatarUrl);
    }

    private static ContactConnectionService CreateService(IdentityDbContext context) =>
        new(
            new EfContactConnectionRepository(context),
            new EfUserRepository(context),
            context,
            TimeProvider.System);

    private static async Task<(User Requester, User Target)> AddProfilesAsync(IdentityDbContext context)
    {
        var requester = User.Create(Guid.NewGuid(), "Requester", null, DateTimeOffset.UtcNow);
        var target = User.Create(Guid.NewGuid(), "Target", null, DateTimeOffset.UtcNow);
        context.Profiles.AddRange(requester, target);
        await context.SaveChangesAsync();
        return (requester, target);
    }

    private static IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"connections-{Guid.NewGuid():N}")
            .Options;

        return new IdentityDbContext(options);
    }
}

using Nevma.Contracts.Identity;
using Nevma.Identity.Api.Application.Users;
using Nevma.Identity.Api.Domain.Connections;
using Nevma.ServiceDefaults.Errors;
using ContractStatus = Nevma.Contracts.Identity.ContactConnectionStatus;
using DomainStatus = Nevma.Identity.Api.Domain.Connections.ContactConnectionStatus;

namespace Nevma.Identity.Api.Application.Connections;

public sealed class ContactConnectionService(
    IContactConnectionRepository connections,
    IUserRepository users,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<ContactConnectionResponse>> RequestAsync(
        Guid requesterId,
        CreateContactConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (requesterId == request.TargetUserId)
            return Result<ContactConnectionResponse>.Failure(ContactConnectionErrors.SelfConnection);

        if (await users.GetByIdAsync(request.TargetUserId, cancellationToken) is null)
            return Result<ContactConnectionResponse>.Failure(ContactConnectionErrors.TargetNotFound);

        var existing = await connections.GetByUsersAsync(requesterId, request.TargetUserId, cancellationToken);
        if (existing is not null)
        {
            if (!existing.Reopen(requesterId, request.TargetUserId, timeProvider.GetUtcNow()))
                return Result<ContactConnectionResponse>.Failure(ContactConnectionErrors.AlreadyExists);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<ContactConnectionResponse>.Success(ToResponse(existing));
        }

        var connection = ContactConnection.Create(requesterId, request.TargetUserId, timeProvider.GetUtcNow());
        await connections.AddAsync(connection, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ContactConnectionResponse>.Success(ToResponse(connection));
    }

    public Task<Result<ContactConnectionResponse>> AcceptAsync(
        Guid connectionId,
        Guid actorId,
        CancellationToken cancellationToken = default) =>
        DecideAsync(connectionId, actorId, accept: true, cancellationToken);

    public Task<Result<ContactConnectionResponse>> RejectAsync(
        Guid connectionId,
        Guid actorId,
        CancellationToken cancellationToken = default) =>
        DecideAsync(connectionId, actorId, accept: false, cancellationToken);

    public async Task<IReadOnlyList<ContactConnectionResponse>> ListAsync(
        Guid userId,
        ContractStatus? status,
        CancellationToken cancellationToken = default)
    {
        DomainStatus? domainStatus = status is null ? null : (DomainStatus)(int)status.Value;
        var results = await connections.ListForUserAsync(userId, domainStatus, cancellationToken);
        return results.Select(ToResponse).ToArray();
    }

    public async Task<IReadOnlyList<ContactSummary>> ListContactsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var accepted = await connections.ListForUserAsync(userId, DomainStatus.Accepted, cancellationToken);
        var contactIds = accepted
            .Select(connection => connection.RequesterId == userId ? connection.AddresseeId : connection.RequesterId)
            .Distinct()
            .ToArray();
        var profiles = await users.GetByIdsAsync(contactIds, cancellationToken);
        return profiles.Select(user => new ContactSummary(user.Id, user.DisplayName, user.AvatarUrl)).ToArray();
    }

    private async Task<Result<ContactConnectionResponse>> DecideAsync(
        Guid connectionId,
        Guid actorId,
        bool accept,
        CancellationToken cancellationToken)
    {
        var connection = await connections.GetByIdAsync(connectionId, cancellationToken);
        if (connection is null)
            return Result<ContactConnectionResponse>.Failure(ContactConnectionErrors.NotFound);

        var transition = accept
            ? connection.Accept(actorId, timeProvider.GetUtcNow())
            : connection.Reject(actorId, timeProvider.GetUtcNow());

        if (transition == ConnectionTransition.Forbidden)
            return Result<ContactConnectionResponse>.Failure(ContactConnectionErrors.Forbidden);

        if (transition == ConnectionTransition.AlreadyHandled)
            return Result<ContactConnectionResponse>.Failure(ContactConnectionErrors.AlreadyHandled);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ContactConnectionResponse>.Success(ToResponse(connection));
    }

    private static ContactConnectionResponse ToResponse(ContactConnection connection) =>
        new(
            connection.Id,
            connection.RequesterId,
            connection.AddresseeId,
            (ContractStatus)(int)connection.Status,
            connection.CreatedAt,
            connection.RespondedAt);
}

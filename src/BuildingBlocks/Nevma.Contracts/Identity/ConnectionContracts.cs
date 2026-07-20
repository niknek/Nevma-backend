namespace Nevma.Contracts.Identity;

public sealed record CreateContactConnectionRequest(Guid TargetUserId);

public sealed record ContactSummary(Guid UserId, string DisplayName, string? AvatarUrl);

public sealed record SaveAddressBookContactRequest(
    string Name,
    string Phone,
    string? Email,
    string? Social,
    string? Address,
    string? Notes);

public sealed record AddressBookContactResponse(
    Guid Id,
    string Name,
    string Phone,
    string? Email,
    string? Social,
    string? Address,
    string? Notes,
    Guid? LinkedUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ContactConnectionResponse(
    Guid Id,
    Guid RequesterId,
    Guid AddresseeId,
    ContactConnectionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RespondedAt);

public enum ContactConnectionStatus
{
    Pending,
    Accepted,
    Rejected
}

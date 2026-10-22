using Nevma.Contracts.Identity;
using Nevma.Identity.Api.Domain.Users;

namespace Nevma.Identity.Api.Application.Users;

public sealed class UserService(
    IUserRepository repository,
    IIdentityUnitOfWork unitOfWork)
{
    public async Task<UserSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await repository.GetByIdAsync(id, cancellationToken);
        return user is null ? null : ToSummary(user);
    }

    public async Task<UpdateProfileResult> UpdateAsync(
        Guid userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 100)
            errors[nameof(request.DisplayName)] = ["Display name must contain 1 to 100 characters."];
        if (request.AvatarUrl?.Length > 2_048 ||
            (request.AvatarUrl is not null && !Uri.TryCreate(request.AvatarUrl, UriKind.Absolute, out _)))
            errors[nameof(request.AvatarUrl)] = ["Avatar URL must be a valid absolute URL."];
        if (request.Bio?.Length > 300)
            errors[nameof(request.Bio)] = ["Bio must contain at most 300 characters."];
        if (request.ContactEmail is not null &&
            (!System.Net.Mail.MailAddress.TryCreate(request.ContactEmail, out _) || request.ContactEmail.Length > 320))
            errors[nameof(request.ContactEmail)] = ["Contact email must be valid."];
        if (request.Phone?.Length > 30)
            errors[nameof(request.Phone)] = ["Phone must contain at most 30 characters."];
        if (request.Availability is not ("Available" or "Busy" or "Away" or "Offline"))
            errors[nameof(request.Availability)] = ["Availability is invalid."];
        if (request.StoryPrivacy is not ("Contacts" or "Private"))
            errors[nameof(request.StoryPrivacy)] = ["Story privacy is invalid."];
        if (errors.Count > 0)
            return new UpdateProfileResult.Invalid(errors);

        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return new UpdateProfileResult.NotFound();
        user.UpdateProfile(request.DisplayName, request.AvatarUrl, request.Bio, request.ContactEmail,
            request.Phone, request.ShowEmail, request.ShowPhone, request.Availability, request.StoryPrivacy);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new UpdateProfileResult.Updated(ToSummary(user));
    }

    private static UserSummary ToSummary(User user) =>
        new(user.Id, user.DisplayName, user.AvatarUrl, user.Bio, user.ContactEmail, user.Phone,
            user.ShowEmail, user.ShowPhone, user.Availability, user.StoryPrivacy);
}

public abstract record UpdateProfileResult
{
    public sealed record Updated(UserSummary User) : UpdateProfileResult;
    public sealed record NotFound : UpdateProfileResult;
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : UpdateProfileResult;
}

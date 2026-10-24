namespace Nevma.Identity.Api.Application.Authentication;

public interface IAccountRecoveryService
{
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);
    Task<AccountRecoveryResult> ResetPasswordAsync(string email, string encodedToken, string newPassword);
    Task RequestEmailConfirmationAsync(Guid userId, CancellationToken cancellationToken = default);
    Task RequestEmailConfirmationAsync(string email, CancellationToken cancellationToken = default);
    Task<AccountRecoveryResult> ConfirmEmailAsync(string email, string encodedToken);
}

public abstract record AccountRecoveryResult
{
    public sealed record Succeeded : AccountRecoveryResult;
    public sealed record Failed(IReadOnlyDictionary<string, string[]> Errors) : AccountRecoveryResult;
    public sealed record InvalidToken : AccountRecoveryResult;

    public static AccountRecoveryResult Success { get; } = new Succeeded();
    public static AccountRecoveryResult Invalid { get; } = new InvalidToken();
}

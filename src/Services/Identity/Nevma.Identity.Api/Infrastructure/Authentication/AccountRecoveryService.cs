using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Nevma.Identity.Api.Application.Authentication;
using Nevma.Identity.Api.Infrastructure.Persistence;

namespace Nevma.Identity.Api.Infrastructure.Authentication;

public sealed class AccountRecoveryService(
    UserManager<IdentityAccount> userManager,
    IIdentityEmailSender emailSender) : IAccountRecoveryService
{
    public async Task RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var account = await userManager.FindByEmailAsync(email.Trim());
        if (account is null)
            return;

        var token = await userManager.GeneratePasswordResetTokenAsync(account);
        await emailSender.SendPasswordResetAsync(
            account.Email ?? email.Trim(),
            Encode(token),
            cancellationToken);
    }

    public async Task<AccountRecoveryResult> ResetPasswordAsync(
        string email,
        string encodedToken,
        string newPassword)
    {
        var account = await userManager.FindByEmailAsync(email.Trim());
        if (account is null || !TryDecode(encodedToken, out var token))
            return AccountRecoveryResult.Invalid;

        var result = await userManager.ResetPasswordAsync(account, token, newPassword);
        return result.Succeeded
            ? AccountRecoveryResult.Success
            : new AccountRecoveryResult.Failed(result.Errors
                .GroupBy(error => error.Code)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Description).ToArray()));
    }

    public async Task RequestEmailConfirmationAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var account = await userManager.FindByIdAsync(userId.ToString());
        if (account?.Email is null || account.EmailConfirmed)
            return;

        var token = await userManager.GenerateEmailConfirmationTokenAsync(account);
        await emailSender.SendEmailConfirmationAsync(
            account.Email,
            Encode(token),
            cancellationToken);
    }

    public async Task RequestEmailConfirmationAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return;
        var account = await userManager.FindByEmailAsync(email.Trim());
        if (account is null)
            return;
        await RequestEmailConfirmationAsync(account.Id, cancellationToken);
    }

    public async Task<AccountRecoveryResult> ConfirmEmailAsync(
        string email,
        string encodedToken)
    {
        var account = await userManager.FindByEmailAsync(email.Trim());
        if (account is null || !TryDecode(encodedToken, out var token))
            return AccountRecoveryResult.Invalid;

        var result = await userManager.ConfirmEmailAsync(account, token);
        return result.Succeeded
            ? AccountRecoveryResult.Success
            : AccountRecoveryResult.Invalid;
    }

    private static string Encode(string token) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static bool TryDecode(string encodedToken, out string token)
    {
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
            return true;
        }
        catch (FormatException)
        {
            token = string.Empty;
            return false;
        }
    }
}

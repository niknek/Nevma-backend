using System.Security.Claims;
using Nevma.Contracts.Identity;
using Nevma.Identity.Api.Application.Authentication;
using Nevma.Identity.Api.Infrastructure.Authentication;

namespace Nevma.Identity.Api.Endpoints;

public static class AccountRecoveryEndpoints
{
    public static IEndpointRouteBuilder MapAccountRecoveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var recovery = endpoints.MapGroup("/api/auth")
            .WithTags("Account recovery");

        recovery.MapPost("/password-reset/request", async (
            RequestPasswordResetRequest request,
            IAccountRecoveryService service,
            CancellationToken cancellationToken) =>
        {
            if (!string.IsNullOrWhiteSpace(request.Email))
                await service.RequestPasswordResetAsync(request.Email, cancellationToken);
            return Results.Accepted();
        })
        .AllowAnonymous()
        .RequireRateLimiting(AuthenticationConfiguration.AuthenticationRateLimitPolicy);

        recovery.MapPost("/password-reset/confirm", async (
            ResetPasswordRequest request,
            IAccountRecoveryService service) =>
            ToResult(await service.ResetPasswordAsync(
                request.Email,
                request.Token,
                request.NewPassword)))
        .AllowAnonymous()
        .RequireRateLimiting(AuthenticationConfiguration.AuthenticationRateLimitPolicy);

        recovery.MapPost("/email-confirmation/request", async (
            RequestEmailConfirmationRequest request,
            IAccountRecoveryService service,
            CancellationToken cancellationToken) =>
        {
            await service.RequestEmailConfirmationAsync(request.Email, cancellationToken);
            return Results.Accepted();
        })
        .AllowAnonymous()
        .RequireRateLimiting(AuthenticationConfiguration.AuthenticationRateLimitPolicy);

        recovery.MapPost("/email-confirmation/confirm", async (
            ConfirmEmailRequest request,
            IAccountRecoveryService service) =>
            ToResult(await service.ConfirmEmailAsync(request.Email, request.Token)))
        .AllowAnonymous()
        .RequireRateLimiting(AuthenticationConfiguration.AuthenticationRateLimitPolicy);

        return endpoints;
    }

    private static IResult ToResult(AccountRecoveryResult result) => result switch
    {
        AccountRecoveryResult.Succeeded => Results.NoContent(),
        AccountRecoveryResult.Failed failed => Results.ValidationProblem(failed.Errors),
        AccountRecoveryResult.InvalidToken => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Token"] = ["The account recovery token is invalid or expired."]
        }),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };
}

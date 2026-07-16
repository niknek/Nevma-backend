using System.Security.Claims;
using Nevma.Contracts.Identity;
using Nevma.Identity.Api.Application.Authentication;
using Nevma.Identity.Api.Infrastructure.Authentication;
using Nevma.Identity.Api.Application.Security;
using OpenIddict.Abstractions;

namespace Nevma.Identity.Api.Endpoints;

public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/register", async (
            RegisterUserRequest request,
            IRegistrationService service,
            IAccountRecoveryService recoveryService,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RegisterAsync(request, cancellationToken);
            if (result.IsSuccess && result.User is not null)
                await recoveryService.RequestEmailConfirmationAsync(result.User.Id, cancellationToken);
            return result.IsSuccess
                ? Results.Created("/api/users/me", result.User)
                : Results.ValidationProblem(result.Errors);
        })
        .AllowAnonymous()
        .RequireRateLimiting(AuthenticationConfiguration.AuthenticationRateLimitPolicy)
        .WithTags("Authentication");

        endpoints.MapPost("/api/auth/logout-all", async (
            ClaimsPrincipal principal,
            HttpContext context,
            IOpenIddictTokenManager tokenManager,
            IOpenIddictAuthorizationManager authorizationManager,
            SecurityEventService securityEvents,
            CancellationToken cancellationToken) =>
        {
            var subject = principal.FindFirstValue(OpenIddictConstants.Claims.Subject);
            if (string.IsNullOrWhiteSpace(subject))
                return Results.Unauthorized();

            await tokenManager.RevokeBySubjectAsync(subject, cancellationToken);
            await authorizationManager.RevokeBySubjectAsync(subject, cancellationToken);
            if (Guid.TryParse(subject, out var userId))
                await securityEvents.RecordFromRequestAsync(userId, "sessions.revoked_all", true, context);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags("Authentication");

        return endpoints;
    }
}

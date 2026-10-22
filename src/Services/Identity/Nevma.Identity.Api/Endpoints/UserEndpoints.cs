using System.Security.Claims;
using Nevma.Contracts.Identity;
using Nevma.Identity.Api.Application.Users;
using OpenIddict.Abstractions;

namespace Nevma.Identity.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapGroup("/api/users").WithTags("Users");
        if (!endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            users.RequireAuthorization();

        users.MapGet("/me", async (
            ClaimsPrincipal principal,
            UserService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();

            var user = await service.GetByIdAsync(userId, cancellationToken);
            return user is null ? Results.NotFound() : Results.Ok(user);
        });

        users.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            UserService service,
            UserPrivacyService privacyService,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var viewerId))
                return Results.Unauthorized();
            if (viewerId != id && await privacyService.IsBlockedAsync(viewerId, id, cancellationToken))
                return Results.NotFound();
            var user = await service.GetByIdAsync(id, cancellationToken);
            if (user is null) return Results.NotFound();
            if (viewerId != id)
                user = user with
                {
                    ContactEmail = user.ShowEmail ? user.ContactEmail : null,
                    Phone = user.ShowPhone ? user.Phone : null
                };
            return Results.Ok(user);
        });

        users.MapPut("/me", async (
            UpdateProfileRequest request,
            ClaimsPrincipal principal,
            UserService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();
            return await service.UpdateAsync(userId, request, cancellationToken) switch
            {
                UpdateProfileResult.Updated updated => Results.Ok(updated.User),
                UpdateProfileResult.NotFound => Results.NotFound(),
                UpdateProfileResult.Invalid invalid => Results.ValidationProblem(invalid.Errors),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        });

        users.MapGet("/me/settings", async (
            ClaimsPrincipal principal,
            UserPrivacyService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();
            return Results.Ok(await service.GetSettingsAsync(userId, cancellationToken));
        });

        users.MapPut("/me/settings", async (
            UpdateUserSettingsRequest request,
            ClaimsPrincipal principal,
            UserPrivacyService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();
            return await service.UpdateSettingsAsync(userId, request, cancellationToken) switch
            {
                SettingsUpdateResult.Updated updated => Results.Ok(updated.Settings),
                SettingsUpdateResult.Invalid invalid => Results.ValidationProblem(invalid.Errors),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        });

        users.MapPost("/{id:guid}/block", async (
            Guid id,
            ClaimsPrincipal principal,
            UserPrivacyService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();
            return await service.BlockAsync(userId, id, cancellationToken)
                ? Results.NoContent()
                : Results.BadRequest(new { message = "User cannot be blocked." });
        });

        users.MapDelete("/{id:guid}/block", async (
            Guid id,
            ClaimsPrincipal principal,
            UserPrivacyService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();
            return await service.UnblockAsync(userId, id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        });

        users.MapPost("/{id:guid}/report", async (
            Guid id,
            ReportUserRequest request,
            ClaimsPrincipal principal,
            UserPrivacyService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();
            return await service.ReportAsync(userId, id, request, cancellationToken) switch
            {
                ReportUserResult.Accepted accepted => Results.Accepted(value: new { accepted.ReportId }),
                ReportUserResult.NotFound => Results.NotFound(),
                ReportUserResult.Invalid => Results.BadRequest(new { message = "Report reason or details are invalid." }),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        });

        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(OpenIddictConstants.Claims.Subject), out userId);
}

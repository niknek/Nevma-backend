using System.Security.Claims;
using Nevma.Contracts.Identity;
using Nevma.Identity.Api.Application.Connections;
using Nevma.ServiceDefaults.Errors;

namespace Nevma.Identity.Api.Endpoints;

public static class ContactConnectionEndpoints
{
    public static IEndpointRouteBuilder MapContactConnectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var connections = endpoints.MapGroup("/api/connections")
            .RequireAuthorization()
            .WithTags("Connections");

        connections.MapPost("/", async (
            CreateContactConnectionRequest request,
            ClaimsPrincipal principal,
            ContactConnectionService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();

            var result = await service.RequestAsync(userId, request, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/connections/{result.Value.Id}", result.Value)
                : ToProblem(result.Error);
        });

        connections.MapPost("/{id:guid}/accept", async (
            Guid id,
            ClaimsPrincipal principal,
            ContactConnectionService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();

            var result = await service.AcceptAsync(id, userId, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
        });

        connections.MapPost("/{id:guid}/reject", async (
            Guid id,
            ClaimsPrincipal principal,
            ContactConnectionService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();

            var result = await service.RejectAsync(id, userId, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
        });

        connections.MapGet("/", async (
            ContactConnectionStatus? status,
            ClaimsPrincipal principal,
            ContactConnectionService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();

            return Results.Ok(await service.ListAsync(userId, status, cancellationToken));
        });

        connections.MapGet("/contacts", async (
            ClaimsPrincipal principal,
            ContactConnectionService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();
            return Results.Ok(await service.ListContactsAsync(userId, cancellationToken));
        });

        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out userId);

    private static IResult ToProblem(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(
            statusCode: statusCode,
            title: error.Description,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}

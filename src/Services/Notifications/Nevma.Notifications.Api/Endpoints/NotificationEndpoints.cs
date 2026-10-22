using Nevma.Contracts.Notifications;
using Nevma.Notifications.Api.Application.Notifications;

namespace Nevma.Notifications.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/api/notifications").WithTags("Notifications");

        notifications.MapGet("/", async (
            int? take,
            HttpContext context,
            NotificationService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(context, out var userId))
                return Results.Unauthorized();
            return Results.Ok(await service.ListAsync(userId, take ?? 50, cancellationToken));
        });

        notifications.MapPost("/{id:guid}/read", async (
            Guid id,
            HttpContext context,
            NotificationService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(context, out var userId))
                return Results.Unauthorized();
            return await service.MarkReadAsync(id, userId, cancellationToken) switch
            {
                MarkNotificationReadResult.Read read => Results.Ok(read.Notification),
                MarkNotificationReadResult.NotFound => Results.NotFound(),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        });

        notifications.MapGet("/preferences", async (
            HttpContext context,
            NotificationPreferenceService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(context, out var userId))
                return Results.Unauthorized();
            return Results.Ok(await service.GetAsync(userId, cancellationToken));
        });

        notifications.MapPut("/preferences", async (
            UpdateNotificationPreferenceRequest request,
            HttpContext context,
            NotificationPreferenceService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(context, out var userId))
                return Results.Unauthorized();
            return await service.UpdateAsync(userId, request, cancellationToken) switch
            {
                PreferenceUpdateResult.Updated updated => Results.Ok(updated.Preference),
                PreferenceUpdateResult.Invalid invalid => Results.ValidationProblem(invalid.Errors),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        });

        return endpoints;
    }

    private static bool TryGetUserId(HttpContext context, out Guid userId) =>
        Guid.TryParse(context.User.FindFirst("sub")?.Value, out userId) ||
        (context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() &&
         Guid.TryParse(context.Request.Headers["X-Nevma-User-Id"], out userId));
}

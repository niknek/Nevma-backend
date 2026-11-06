using Nevma.Contracts.Notifications;
using Nevma.Notifications.Api.Application.Notifications;
using Microsoft.EntityFrameworkCore;
using Nevma.Notifications.Api.Infrastructure.Persistence;

namespace Nevma.Notifications.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/api/notifications").WithTags("Notifications");
        notifications.MapPost("/internal/contact-joined", async (ContactJoinedRequest request, HttpContext context,
            IConfiguration configuration, NotificationsDbContext db, NotificationService service, CancellationToken cancellationToken) =>
        {
            var expected = configuration["InternalServices:Key"];
            if (string.IsNullOrWhiteSpace(expected) || context.Request.Headers["X-Nevma-Internal-Key"] != expected)
                return Results.Unauthorized();
            if (request.OwnerId == Guid.Empty || request.JoinedUserId == Guid.Empty ||
                string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 150)
                return Results.BadRequest();
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var lockKey = BitConverter.ToInt64(request.OwnerId.ToByteArray());
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
                if (!await db.Notifications.AnyAsync(item => item.UserId == request.OwnerId &&
                    item.Type == "contact.joined" && item.ResourceId == request.JoinedUserId, cancellationToken))
                    await service.CreatePushAsync(request.OwnerId, "contact.joined", "Νέα επαφή στο Nevma",
                        $"{request.DisplayName} είναι πλέον στο Nevma. Μπορείτε να συνομιλήσετε!", request.JoinedUserId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
            return Results.Accepted();
        }).AllowAnonymous();

        notifications.MapPost("/internal/chat", async (ChatPushRequest request, HttpContext context,
            IConfiguration configuration, NotificationService service, CancellationToken cancellationToken) =>
        {
            var expected = configuration["InternalServices:Key"];
            if (string.IsNullOrWhiteSpace(expected) || context.Request.Headers["X-Nevma-Internal-Key"] != expected)
                return Results.Unauthorized();
            await service.CreatePushAsync(request.UserId, "chat.message", "Nevma · νέο μήνυμα",
                $"{request.SenderName} σου έστειλε μήνυμα.", request.ConversationId, cancellationToken);
            return Results.Accepted();
        }).AllowAnonymous();

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

        notifications.MapPost("/read-all", async (HttpContext context,
            NotificationsDbContext db, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(context, out var userId)) return Results.Unauthorized();
            var readAt = clock.GetUtcNow();
            await db.Notifications.Where(item => item.UserId == userId && item.ReadAt == null)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.ReadAt, readAt), cancellationToken);
            return Results.Ok(new { readAt });
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

public sealed record ChatPushRequest(Guid UserId, Guid ConversationId, string SenderName);
public sealed record ContactJoinedRequest(Guid OwnerId, Guid JoinedUserId, string DisplayName);

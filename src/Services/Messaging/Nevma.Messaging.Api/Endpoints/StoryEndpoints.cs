using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nevma.Messaging.Api.Domain.Stories;
using Nevma.Messaging.Api.Infrastructure.Persistence;

namespace Nevma.Messaging.Api.Endpoints;

public static class StoryEndpoints
{
    public static IEndpointRouteBuilder MapStoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var stories = endpoints.MapGroup("/api/stories").RequireAuthorization().WithTags("Stories");

        stories.MapGet("/", async (ClaimsPrincipal principal, MessagingDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId)) return Results.Unauthorized();
            var now = clock.GetUtcNow();
            var items = await db.Stories.AsNoTracking()
                .Where(x => x.ExpiresAt > now && (x.OwnerId == userId || x.AudienceIds.Contains(userId)))
                .OrderBy(x => x.CreatedAt).ToListAsync(ct);
            return Results.Ok(items.Select(ToResponse));
        });

        stories.MapPost("/", async (CreateStoryRequest request, ClaimsPrincipal principal, MessagingDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId)) return Results.Unauthorized();
            if (request.FileAssetId == Guid.Empty || string.IsNullOrWhiteSpace(request.MediaType) || request.Caption?.Length > 500)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["story"] = ["Valid media and a caption up to 500 characters are required."] });
            var item = Story.Create(userId, request.FileAssetId, request.MediaType, request.Caption, request.AudienceIds ?? [], clock.GetUtcNow(), request.IsRestrictedAudience);
            db.Stories.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/stories/{item.Id}", ToResponse(item));
        });

        stories.MapPost("/{id:guid}/seen", async (Guid id, ClaimsPrincipal principal, MessagingDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId)) return Results.Unauthorized();
            var item = await db.Stories.SingleOrDefaultAsync(x => x.Id == id && x.ExpiresAt > clock.GetUtcNow(), ct);
            if (item is null || (item.OwnerId != userId && !item.AudienceIds.Contains(userId))) return Results.NotFound();
            item.MarkSeen(userId);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToResponse(item));
        });

        stories.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, MessagingDbContext db, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId)) return Results.Unauthorized();
            var item = await db.Stories.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == userId, ct);
            if (item is null) return Results.NotFound();
            db.Stories.Remove(item);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        return endpoints;
    }

    private static object ToResponse(Story x) => new { x.Id, x.OwnerId, x.FileAssetId, x.MediaType, x.Caption, x.SeenByIds, x.IsRestrictedAudience, x.CreatedAt, x.ExpiresAt };
    private static bool TryUser(ClaimsPrincipal principal, out Guid userId) => Guid.TryParse(principal.FindFirstValue("sub"), out userId);
}

public sealed record CreateStoryRequest(Guid FileAssetId, string MediaType, string? Caption, IReadOnlyCollection<Guid>? AudienceIds, bool IsRestrictedAudience = false);

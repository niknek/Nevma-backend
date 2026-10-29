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
            return Results.Ok(items.Select(x => ToResponse(x, userId)));
        });

        stories.MapPost("/", async (CreateStoryRequest request, ClaimsPrincipal principal, MessagingDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId)) return Results.Unauthorized();
            if (request.FileAssetId == Guid.Empty || string.IsNullOrWhiteSpace(request.MediaType) || request.Caption?.Length > 500)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["story"] = ["Valid media and a caption up to 500 characters are required."] });
            var item = Story.Create(userId, request.FileAssetId, request.MediaType, request.Caption, request.AudienceIds ?? [], clock.GetUtcNow(), request.IsRestrictedAudience);
            db.Stories.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/stories/{item.Id}", ToResponse(item, userId));
        });

        stories.MapPost("/{id:guid}/seen", async (Guid id, ClaimsPrincipal principal, MessagingDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId)) return Results.Unauthorized();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var rows = await db.Stories.FromSqlInterpolated($"SELECT * FROM messaging.stories WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(ct);
            var item = rows.SingleOrDefault();
            if (item is null || item.ExpiresAt <= clock.GetUtcNow() || (item.OwnerId != userId && !item.AudienceIds.Contains(userId))) return Results.NotFound();
            item.MarkSeen(userId);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(ToResponse(item, userId));
        });

        stories.MapPut("/{id:guid}/like", async (Guid id, StoryLikeRequest request, ClaimsPrincipal principal, MessagingDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId)) return Results.Unauthorized();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var rows = await db.Stories.FromSqlInterpolated($"SELECT * FROM messaging.stories WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(ct);
            var item = rows.SingleOrDefault();
            if (item is null || item.ExpiresAt <= clock.GetUtcNow() || !item.AudienceIds.Contains(userId)) return Results.NotFound();
            item.SetLike(userId, request.Liked);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(ToResponse(item, userId));
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

    private static object ToResponse(Story x, Guid viewerId) => new { x.Id, x.OwnerId, x.FileAssetId, x.MediaType, x.Caption, SeenByIds = x.OwnerId == viewerId ? x.SeenByIds : x.SeenByIds.Where(id => id == viewerId).ToArray(), LikedByIds = x.OwnerId == viewerId ? x.LikedByIds : x.LikedByIds.Where(id => id == viewerId).ToArray(), ViewCount = x.SeenByIds.Length, LikeCount = x.LikedByIds.Length, x.IsRestrictedAudience, x.CreatedAt, x.ExpiresAt };
    private static bool TryUser(ClaimsPrincipal principal, out Guid userId) => Guid.TryParse(principal.FindFirstValue("sub"), out userId);
}

public sealed record CreateStoryRequest(Guid FileAssetId, string MediaType, string? Caption, IReadOnlyCollection<Guid>? AudienceIds, bool IsRestrictedAudience = false);
public sealed record StoryLikeRequest(bool Liked);

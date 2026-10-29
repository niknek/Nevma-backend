using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nevma.Identity.Api.Domain.Users;
using Nevma.Identity.Api.Infrastructure.Persistence;

namespace Nevma.Identity.Api.Endpoints;

public static class StoryAudienceGroupEndpoints
{
    public static IEndpointRouteBuilder MapStoryAudienceGroupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/story-audiences").WithTags("Story audiences");
        if (!endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment()) group.RequireAuthorization();
        group.MapGet("/", async (HttpContext ctx, IdentityDbContext db, CancellationToken ct) =>
        {
            if (!TryUser(ctx, out var owner)) return Results.Unauthorized();
            return Results.Ok(await db.StoryAudienceGroups.AsNoTracking().Where(x => x.OwnerId == owner).OrderBy(x => x.Name).ToListAsync(ct));
        });
        group.MapPost("/", async (SaveStoryAudienceGroup request, HttpContext ctx, IdentityDbContext db, CancellationToken ct) =>
        {
            if (!TryUser(ctx, out var owner)) return Results.Unauthorized();
            if (!Valid(request)) return Results.BadRequest();
            var item = new StoryAudienceGroup { Id = Guid.NewGuid(), OwnerId = owner, Name = request.Name.Trim(), MemberIds = request.MemberIds.Where(x => x != Guid.Empty && x != owner).Distinct().ToArray() };
            db.StoryAudienceGroups.Add(item); await db.SaveChangesAsync(ct); return Results.Ok(item);
        });
        group.MapPut("/{id:guid}", async (Guid id, SaveStoryAudienceGroup request, HttpContext ctx, IdentityDbContext db, CancellationToken ct) =>
        {
            if (!TryUser(ctx, out var owner)) return Results.Unauthorized();
            if (!Valid(request)) return Results.BadRequest();
            var item = await db.StoryAudienceGroups.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == owner, ct);
            if (item is null) return Results.NotFound();
            item.Name = request.Name.Trim(); item.MemberIds = request.MemberIds.Where(x => x != Guid.Empty && x != owner).Distinct().ToArray();
            await db.SaveChangesAsync(ct); return Results.Ok(item);
        });
        group.MapDelete("/{id:guid}", async (Guid id, HttpContext ctx, IdentityDbContext db, CancellationToken ct) =>
        {
            if (!TryUser(ctx, out var owner)) return Results.Unauthorized();
            var item = await db.StoryAudienceGroups.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == owner, ct);
            if (item is null) return Results.NotFound();
            db.StoryAudienceGroups.Remove(item); await db.SaveChangesAsync(ct); return Results.NoContent();
        });
        return endpoints;
    }
    private static bool Valid(SaveStoryAudienceGroup request) => !string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim().Length <= 80 && request.MemberIds is { Length: <= 1000 };
    private static bool TryUser(HttpContext ctx, out Guid id) => Guid.TryParse(ctx.User.FindFirstValue("sub"), out id) || (ctx.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() && Guid.TryParse(ctx.Request.Headers["X-Nevma-User-Id"], out id));
}

public sealed record SaveStoryAudienceGroup(string Name, Guid[] MemberIds);

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nevma.Planning.Api.Domain.Calendar;
using Nevma.Planning.Api.Infrastructure.Persistence;

namespace Nevma.Planning.Api.Endpoints;

public static class PersonalCalendarEndpoints
{
    public static IEndpointRouteBuilder MapPersonalCalendarEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/personal-calendar").WithTags("Personal calendar");
        group.MapGet("/", async (HttpContext context, PlanningDbContext db, CancellationToken ct) =>
        {
            if (!TryUser(context, out var userId)) return Results.Unauthorized();
            return Results.Ok(await db.PersonalCalendarEvents.Where(x => x.UserId == userId).OrderBy(x => x.StartsAt).ToListAsync(ct));
        });
        group.MapPost("/", async (PersonalEventRequest request, HttpContext context, PlanningDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(context, out var userId)) return Results.Unauthorized();
            var validation = ValidateDate(request, clock.GetUtcNow()); if (validation is not null) return validation;
            var item = PersonalCalendarEvent.Create(userId, request.Title, request.Purpose, request.Location, request.StartsAt, request.EndsAt, clock.GetUtcNow());
            db.PersonalCalendarEvents.Add(item); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/personal-calendar/{item.Id}", item);
        });
        group.MapPut("/{id:guid}", async (Guid id, PersonalEventRequest request, HttpContext context, PlanningDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(context, out var userId)) return Results.Unauthorized();
            var item = await db.PersonalCalendarEvents.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
            if (item is null) return Results.NotFound();
            var validation = ValidateDate(request, clock.GetUtcNow()); if (validation is not null) return validation;
            item.Update(request.Title, request.Purpose, request.Location, request.StartsAt, request.EndsAt, clock.GetUtcNow());
            await db.SaveChangesAsync(ct); return Results.Ok(item);
        });
        return endpoints;
    }

    private static IResult? ValidateDate(PersonalEventRequest request, DateTimeOffset now)
    {
        if (request.StartsAt < now.AddMinutes(-1)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["startsAt"] = ["Appointments cannot be scheduled in the past."] });
        if (request.StartsAt > now.AddYears(10)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["startsAt"] = ["Appointments can be scheduled up to 10 years ahead."] });
        return null;
    }

    private static bool TryUser(HttpContext context, out Guid userId) =>
        Guid.TryParse(context.User.FindFirstValue("sub"), out userId) ||
        (context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() && Guid.TryParse(context.Request.Headers["X-Nevma-User-Id"], out userId));
}

public sealed record PersonalEventRequest(string Title, string? Purpose, string? Location, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

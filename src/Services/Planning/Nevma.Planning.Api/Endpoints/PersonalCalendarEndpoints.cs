using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using Nevma.Contracts.Integration;
using Nevma.Contracts.Planning;
using Nevma.Planning.Api.Application;
using Nevma.Planning.Api.Domain.Calendar;
using Nevma.Planning.Api.Infrastructure.Persistence;

namespace Nevma.Planning.Api.Endpoints;

public static class PersonalCalendarEndpoints
{
    private static readonly ConcurrentDictionary<(Guid EventId, Guid UserId), LiveLocationPointResponse> LiveLocations = new();

    public static IEndpointRouteBuilder MapPersonalCalendarEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/personal-calendar").WithTags("Personal calendar");
        group.MapGet("/", async (HttpContext context, PlanningDbContext db, CancellationToken ct) =>
        {
            if (!TryUser(context, out var userId)) return Results.Unauthorized();
            return Results.Ok(await db.PersonalCalendarEvents
                .Where(x => x.UserId == userId || x.ParticipantIds.Contains(userId))
                .OrderBy(x => x.StartsAt).ToListAsync(ct));
        });
        group.MapPost("/", async (PersonalEventRequest request, HttpContext context, PlanningDbContext db, IPlanningEventOutbox outbox, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(context, out var userId)) return Results.Unauthorized();
            var validation = ValidateDate(request, clock.GetUtcNow()); if (validation is not null) return validation;
            if (await HasActiveConflict(db, userId, request.StartsAt, request.EndsAt, null, ct))
                return ConflictResult();
            var now = clock.GetUtcNow();
            var item = PersonalCalendarEvent.Create(userId, request.Title, request.Purpose, request.Location, request.StartsAt, request.EndsAt, request.ParticipantIds, request.OrganizerSharesLiveLocation, now);
            db.PersonalCalendarEvents.Add(item);
            foreach (var participantId in item.ParticipantIds)
                AddInvitationEvent(outbox, item, participantId, MeetingInvitationStatus.Pending, now);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/personal-calendar/{item.Id}", item);
        });
        group.MapPut("/{id:guid}", async (Guid id, PersonalEventRequest request, HttpContext context, PlanningDbContext db, IPlanningEventOutbox outbox, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(context, out var userId)) return Results.Unauthorized();
            var item = await db.PersonalCalendarEvents.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
            if (item is null) return Results.NotFound();
            if (item.Status is PersonalCalendarEventStatus.Cancelled or PersonalCalendarEventStatus.Declined)
                return Results.Conflict(new { message = "A cancelled or declined appointment cannot be edited." });
            var currentTime = clock.GetUtcNow();
            if (item.StartsAt <= currentTime)
                return Results.Conflict(new { message = "An appointment that has already started cannot be edited." });
            var validation = ValidateDate(request, currentTime); if (validation is not null) return validation;
            var requestedParticipants = (request.ParticipantIds ?? []).ToHashSet();
            if (item.ParticipantIds.Any(participantId => !requestedParticipants.Contains(participantId)))
                return Results.Conflict(new { message = "Existing participants cannot be removed. Cancel this appointment and create a new one instead." });
            if (await HasActiveConflict(db, userId, request.StartsAt, request.EndsAt, item.Id, ct))
                return ConflictResult();
            var previousParticipants = item.ParticipantIds.ToHashSet();
            var now = currentTime;
            item.Update(request.Title, request.Purpose, request.Location, request.StartsAt, request.EndsAt, request.ParticipantIds, request.OrganizerSharesLiveLocation, now);
            foreach (var participantId in item.ParticipantIds.Where(idValue => !previousParticipants.Contains(idValue)))
                AddInvitationEvent(outbox, item, participantId, MeetingInvitationStatus.Pending, now);
            await db.SaveChangesAsync(ct); return Results.Ok(item);
        });
        group.MapPost("/{id:guid}/accept", async (Guid id, HttpContext context, PlanningDbContext db, IPlanningEventOutbox outbox, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(context, out var userId)) return Results.Unauthorized();
            var item = await db.PersonalCalendarEvents.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            var now = clock.GetUtcNow();
            var outcome = item.Accept(userId, now);
            if (outcome == ParticipantResponseOutcome.Forbidden) return Results.Forbid();
            if (outcome == ParticipantResponseOutcome.AlreadyHandled) return Results.Conflict(new { message = "Invitation has already been handled." });
            AddInvitationEvent(outbox, item, userId, MeetingInvitationStatus.Accepted, now);
            await db.SaveChangesAsync(ct);
            return Results.Ok(item);
        });
        group.MapPost("/{id:guid}/decline", async (Guid id, DeclinePersonalEventRequest request, HttpContext context, PlanningDbContext db, IPlanningEventOutbox outbox, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(context, out var userId)) return Results.Unauthorized();
            var item = await db.PersonalCalendarEvents.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            var now = clock.GetUtcNow();
            var outcome = item.Decline(userId, request.Reason, now);
            if (outcome == ParticipantResponseOutcome.Forbidden) return Results.Forbid();
            if (outcome == ParticipantResponseOutcome.AlreadyHandled) return Results.Conflict(new { message = "Invitation has already been handled." });
            if (outcome == ParticipantResponseOutcome.InvalidReason)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["The reason must be up to 500 characters."] });
            AddInvitationEvent(outbox, item, userId, MeetingInvitationStatus.Declined, now, request.Reason);
            await db.SaveChangesAsync(ct);
            return Results.Ok(item);
        });
        group.MapPost("/{id:guid}/cancel", async (Guid id, CancelPersonalEventRequest request, HttpContext context, PlanningDbContext db, IPlanningEventOutbox outbox, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(context, out var userId)) return Results.Unauthorized();
            var item = await db.PersonalCalendarEvents.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
            if (item is null) return Results.NotFound();
            var now = clock.GetUtcNow();
            var outcome = item.Cancel(request.Reason, now);
            if (outcome == ManualCancellationOutcome.InvalidReason)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["A cancellation reason of up to 500 characters is required."] });
            if (outcome == ManualCancellationOutcome.AlreadyHandled)
                return Results.Conflict(new { message = "Appointment has already been handled." });
            outbox.Add(new CalendarEventExpiredIntegrationEvent(
                Guid.NewGuid(), item.Id,
                new[] { item.UserId }.Concat(item.ParticipantIds).Distinct().ToArray(),
                item.Title, item.StartsAt, item.CancellationReason!, now));
            await db.SaveChangesAsync(ct);
            return Results.Ok(item);
        });
        group.MapPost("/{id:guid}/live-location/consent", async (Guid id, LiveLocationConsentRequest request, HttpContext context, PlanningDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(context, out var userId)) return Results.Unauthorized();
            var item = await db.PersonalCalendarEvents.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            var now = clock.GetUtcNow();
            if (item.EndsAt <= now || item.Status is PersonalCalendarEventStatus.Cancelled or PersonalCalendarEventStatus.Declined)
                return Results.Conflict(new { message = "Live location is unavailable for this appointment." });
            if (!item.SetLiveLocationSharing(userId, request.Enabled, now)) return Results.Forbid();
            if (!request.Enabled) LiveLocations.TryRemove((id, userId), out _);
            await db.SaveChangesAsync(ct);
            return Results.Ok(item);
        });
        group.MapPut("/{id:guid}/live-location", async (Guid id, LiveLocationUpdateRequest request, HttpContext context, PlanningDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(context, out var userId)) return Results.Unauthorized();
            var item = await db.PersonalCalendarEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            var now = clock.GetUtcNow();
            if (now < item.StartsAt.AddHours(-1) || now >= item.EndsAt || !item.LiveLocationSharingUserIds.Contains(userId))
                return Results.Conflict(new { message = "Live location is not active." });
            if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180 || request.Accuracy < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["location"] = ["Location coordinates are invalid."] });
            var point = new LiveLocationPointResponse(userId, request.Latitude, request.Longitude, request.Accuracy, now);
            LiveLocations[(id, userId)] = point;
            return Results.Ok(point);
        });
        group.MapGet("/{id:guid}/live-locations", async (Guid id, HttpContext context, PlanningDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!TryUser(context, out var userId)) return Results.Unauthorized();
            var item = await db.PersonalCalendarEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            if (userId != item.UserId && !item.ParticipantIds.Contains(userId)) return Results.Forbid();
            var now = clock.GetUtcNow();
            if (now < item.StartsAt.AddHours(-1) || now >= item.EndsAt) return Results.Ok(Array.Empty<LiveLocationPointResponse>());
            var points = item.LiveLocationSharingUserIds
                .Select(idValue => LiveLocations.TryGetValue((id, idValue), out var point) ? point : null)
                .Where(point => point is not null && now - point.UpdatedAt <= TimeSpan.FromMinutes(1))
                .ToArray();
            return Results.Ok(points);
        });
        return endpoints;
    }

    private static void AddInvitationEvent(
        IPlanningEventOutbox outbox,
        PersonalCalendarEvent item,
        Guid participantId,
        MeetingInvitationStatus status,
        DateTimeOffset now,
        string? responseReason = null) =>
        outbox.Add(new MeetingInvitationChangedIntegrationEvent(
            Guid.NewGuid(), item.Id, item.UserId, participantId, item.Title, item.StartsAt,
            item.EndsAt - item.StartsAt, item.Location, status, null, null, null, item.Id, now,
            responseReason,
            item.AcceptedParticipantIds.Length,
            item.ParticipantIds.Length - item.AcceptedParticipantIds.Length - item.DeclinedParticipantIds.Length,
            item.DeclinedParticipantIds.Length,
            item.LiveLocationSharingUserIds.Contains(item.UserId)));

    private static IResult? ValidateDate(PersonalEventRequest request, DateTimeOffset now)
    {
        if (request.StartsAt < now.AddMinutes(-1)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["startsAt"] = ["Appointments cannot be scheduled in the past."] });
        if (request.StartsAt > now.AddYears(10)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["startsAt"] = ["Appointments can be scheduled up to 10 years ahead."] });
        return null;
    }

    private static Task<bool> HasActiveConflict(
        PlanningDbContext db,
        Guid userId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        Guid? excludedEventId,
        CancellationToken ct)
    {
        var utcStart = startsAt.ToUniversalTime();
        var utcEnd = endsAt.ToUniversalTime();
        return db.PersonalCalendarEvents.AnyAsync(item =>
            (!excludedEventId.HasValue || item.Id != excludedEventId.Value) &&
            (item.UserId == userId || item.ParticipantIds.Contains(userId)) &&
            (item.Status == PersonalCalendarEventStatus.Pending || item.Status == PersonalCalendarEventStatus.Confirmed || item.Status == PersonalCalendarEventStatus.PartiallyAccepted) &&
            item.StartsAt < utcEnd && item.EndsAt > utcStart,
            ct);
    }

    private static IResult ConflictResult() =>
        Results.Conflict(new { message = "Another active appointment overlaps this time." });

    private static bool TryUser(HttpContext context, out Guid userId) =>
        Guid.TryParse(context.User.FindFirstValue("sub"), out userId) ||
        (context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() && Guid.TryParse(context.Request.Headers["X-Nevma-User-Id"], out userId));
}

public sealed record PersonalEventRequest(
    string Title,
    string? Purpose,
    string? Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyCollection<Guid>? ParticipantIds = null,
    bool OrganizerSharesLiveLocation = false);

public sealed record CancelPersonalEventRequest(string Reason);
public sealed record DeclinePersonalEventRequest(string? Reason);
public sealed record LiveLocationConsentRequest(bool Enabled);
public sealed record LiveLocationUpdateRequest(double Latitude, double Longitude, double Accuracy);
public sealed record LiveLocationPointResponse(Guid UserId, double Latitude, double Longitude, double Accuracy, DateTimeOffset UpdatedAt);

using System.Security.Claims;
using Nevma.Contracts.Identity;
using Nevma.Identity.Api.Application.AddressBook;

namespace Nevma.Identity.Api.Endpoints;

public static class AddressBookEndpoints
{
    public static IEndpointRouteBuilder MapAddressBookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/address-book/contacts")
            .WithTags("Address book");
        if (!endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            group.RequireAuthorization();

        group.MapGet("/", async (
            HttpContext context,
            AddressBookService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(context, out var userId)) return Results.Unauthorized();
            return Results.Ok(await service.ListAsync(userId, cancellationToken));
        });

        group.MapPost("/", async (
            SaveAddressBookContactRequest request,
            HttpContext context,
            AddressBookService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(context, out var userId)) return Results.Unauthorized();
            var result = await service.CreateAsync(userId, request, cancellationToken);
            return ToResult(result, contact => Results.Created($"/api/address-book/contacts/{contact.Id}", contact));
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            SaveAddressBookContactRequest request,
            HttpContext context,
            AddressBookService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(context, out var userId)) return Results.Unauthorized();
            var result = await service.UpdateAsync(userId, id, request, cancellationToken);
            return ToResult(result, Results.Ok);
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext context,
            AddressBookService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(context, out var userId)) return Results.Unauthorized();
            return await service.DeleteAsync(userId, id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        });

        return endpoints;
    }

    private static IResult ToResult(
        AddressBookSaveResult result,
        Func<AddressBookContactResponse, IResult> success) => result switch
    {
        AddressBookSaveResult.Saved saved => success(saved.Contact),
        AddressBookSaveResult.Invalid invalid => Results.ValidationProblem(
            new Dictionary<string, string[]> { ["contact"] = [invalid.Message] }),
        AddressBookSaveResult.DuplicatePhone => Results.Conflict(new { message = "A contact with this phone already exists." }),
        AddressBookSaveResult.NotFound => Results.NotFound(),
        _ => Results.Problem()
    };

    private static bool TryGetUserId(HttpContext context, out Guid userId) =>
        Guid.TryParse(context.User.FindFirstValue("sub"), out userId) ||
        (context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() &&
         Guid.TryParse(context.Request.Headers["X-Nevma-User-Id"], out userId));
}

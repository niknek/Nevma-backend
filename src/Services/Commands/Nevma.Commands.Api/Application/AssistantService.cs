using Nevma.Commands.Api.Domain;
using Nevma.Contracts.Commands;

namespace Nevma.Commands.Api.Application;

public sealed class AssistantService(
    IAiAssistantProvider provider,
    ICommandValidator validator,
    ICommandRepository repository,
    ICommandsUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<AssistantResult> RespondAsync(
        Guid userId,
        AssistantRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 2_000)
            return new AssistantResult.Invalid("message", "Message must contain 1 to 2000 characters.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
            return new AssistantResult.Invalid("idempotencyKey", "Idempotency key must contain 1 to 100 characters.");
        if (request.CalendarEvents.Count > 100)
            return new AssistantResult.Invalid("calendarEvents", "At most 100 calendar events can be included.");

        var offsetMinutes = Math.Clamp(request.UtcOffsetMinutes ?? 0, -14 * 60, 14 * 60);
        var localNow = timeProvider.GetUtcNow().ToOffset(TimeSpan.FromMinutes(offsetMinutes));
        var result = await provider.RespondAsync(request, localNow, cancellationToken);
        if (result is AssistantProviderResult.Unavailable unavailable)
            return new AssistantResult.Unavailable(unavailable.Message);

        var completed = (AssistantProviderResult.Completed)result;
        CommandResponse? commandResponse = null;
        if (completed.Command is not null)
        {
            var validation = validator.Validate(completed.Command, localNow);
            if (validation is not ParseResult.Parsed parsed)
                return new AssistantResult.Invalid("message", ((ParseResult.Invalid)validation).Message);

            var existing = await repository.FindByKeyAsync(userId, request.IdempotencyKey, cancellationToken);
            if (existing is not null)
                commandResponse = CommandService.ToResponse(existing);
            else
            {
                var command = CommandRequest.Create(
                    userId,
                    request.IdempotencyKey,
                    request.Message,
                    parsed.Command,
                    timeProvider.GetUtcNow());
                await repository.AddAsync(command, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                commandResponse = CommandService.ToResponse(command);
            }
        }

        return new AssistantResult.Completed(new AssistantResponse(
            completed.Reply,
            completed.DetectedLanguage,
            commandResponse));
    }
}

public abstract record AssistantResult
{
    public sealed record Completed(AssistantResponse Response) : AssistantResult;
    public sealed record Invalid(string Field, string Message) : AssistantResult;
    public sealed record Unavailable(string Message) : AssistantResult;
}

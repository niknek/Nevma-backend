using Nevma.Commands.Api.Domain;
using Nevma.Contracts.Commands;

namespace Nevma.Commands.Api.Application;

public sealed class AssistantService(
    IAiAssistantProvider provider,
    BilingualTemporalNormalizer temporalNormalizer,
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
        var unavailable = result as AssistantProviderResult.Unavailable;
        var completed = result as AssistantProviderResult.Completed ?? new AssistantProviderResult.Completed(
            string.Empty,
            DetectLanguage(request.Message),
            null);
        CommandResponse? commandResponse = null;
        var proposedCommand = completed.Command;
        string? fallbackReply = null;
        if (proposedCommand is null)
        {
            var fallback = temporalNormalizer.TryCreateFallback(
                request.Message, localNow, completed.DetectedLanguage, request.Contacts);
            proposedCommand = fallback.Command;
            if (proposedCommand is null && fallback.Clarification is not null)
                return new AssistantResult.Completed(new AssistantResponse(
                    fallback.Clarification, completed.DetectedLanguage, null, fallback.NeedsMoreInformation));
            if (proposedCommand is null && unavailable is not null)
                return new AssistantResult.Unavailable(unavailable.Message);
            if (proposedCommand is not null)
                fallbackReply = completed.DetectedLanguage == "el"
                    ? "Ετοίμασα το ραντεβού. Έλεγξε τα στοιχεία και επιβεβαίωσέ το."
                    : "I prepared the appointment. Review the details and confirm it.";
        }

        if (proposedCommand is not null)
        {
            var normalization = temporalNormalizer.Normalize(
                request.Message, proposedCommand, localNow, completed.DetectedLanguage, request.Contacts);
            if (normalization.Command is null)
                return new AssistantResult.Completed(new AssistantResponse(
                    normalization.Clarification!, completed.DetectedLanguage, null, normalization.NeedsMoreInformation));

            var validation = validator.Validate(normalization.Command, localNow);
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
            fallbackReply ?? completed.Reply,
            completed.DetectedLanguage,
            commandResponse));
    }

    private static string DetectLanguage(string message) =>
        message.Any(character => character is >= '\u0370' and <= '\u03ff') ? "el" : "en";
}

public abstract record AssistantResult
{
    public sealed record Completed(AssistantResponse Response) : AssistantResult;
    public sealed record Invalid(string Field, string Message) : AssistantResult;
    public sealed record Unavailable(string Message) : AssistantResult;
}

namespace Nevma.Contracts.Commands;

public sealed record PreviewCommandRequest(string Transcript, string IdempotencyKey);

public sealed record AssistantRequest(
    string Message,
    string IdempotencyKey,
    string? Locale,
    int? UtcOffsetMinutes,
    string? SelectedDate,
    IReadOnlyCollection<AssistantCalendarEvent> CalendarEvents,
    IReadOnlyCollection<AssistantContact>? Contacts = null);

public sealed record AssistantContact(Guid UserId, string DisplayName);

public sealed record AssistantCalendarEvent(
    Guid Id,
    string Title,
    string? Purpose,
    string? Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public sealed record AssistantResponse(
    string Reply,
    string DetectedLanguage,
    CommandResponse? ProposedCommand);

public sealed record SpeechTranscriptionResponse(string Transcript, double? Confidence);

public sealed record CommandResponse(
    Guid Id,
    CommandIntent Intent,
    CommandStatus Status,
    string Summary,
    string ArgumentsJson,
    bool RequiresConfirmation,
    string? ResultResource,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExecutedAt);

public enum CommandIntent
{
    CreateTask,
    CompleteTask,
    DeleteTask,
    ShareTask,
    CreateMeeting,
    AcceptMeeting,
    DeclineMeeting,
    CancelMeeting,
    SendMessage,
    CreateCalendarEvent,
    UpdateCalendarEvent
}
public enum CommandStatus { Previewed, Executing, Executed, Failed, Undone }

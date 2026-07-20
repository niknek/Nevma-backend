using Nevma.Contracts.Commands;
using System.Security.Cryptography;
using System.Text;

namespace Nevma.Commands.Api.Domain;

public sealed class CommandRequest
{
    private CommandRequest(
        Guid id, Guid userId, string idempotencyKey, string transcriptHash, CommandIntent intent,
        string summary, string argumentsJson, DateTimeOffset createdAt)
    {
        Id = id; UserId = userId; IdempotencyKey = idempotencyKey; TranscriptHash = transcriptHash;
        Intent = intent; Summary = summary; ArgumentsJson = argumentsJson; CreatedAt = createdAt;
        Status = CommandStatus.Previewed;
    }

    public Guid Id { get; }
    public Guid UserId { get; }
    public string IdempotencyKey { get; }
    public string TranscriptHash { get; }
    public CommandIntent Intent { get; }
    public string Summary { get; }
    public string ArgumentsJson { get; }
    public CommandStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? ExecutedAt { get; private set; }
    public string? ResultResource { get; private set; }
    public string? Error { get; private set; }

    public static CommandRequest Create(Guid userId, string key, string transcript, ParsedCommand parsed, DateTimeOffset now) =>
        new(Guid.NewGuid(), userId, key.Trim(),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transcript.Trim()))).ToLowerInvariant(),
            parsed.Intent, parsed.Summary, parsed.ArgumentsJson, now);

    public bool BeginExecution(DateTimeOffset now)
    {
        if (Status != CommandStatus.Previewed) return false;
        Status = CommandStatus.Executing; ConfirmedAt = now; return true;
    }

    public void Complete(string resource, DateTimeOffset now)
    {
        Status = CommandStatus.Executed; ResultResource = resource; ExecutedAt = now; Error = null;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        Status = CommandStatus.Failed; Error = error.Length <= 500 ? error : error[..500]; ExecutedAt = now;
    }

    public bool MarkUndone(DateTimeOffset now)
    {
        if (Status != CommandStatus.Executed) return false;
        Status = CommandStatus.Undone; ExecutedAt = now; return true;
    }
}

public sealed record ParsedCommand(CommandIntent Intent, string Summary, string ArgumentsJson);
public sealed record ResourceCommandArguments(Guid Id);
public sealed record SendMessageCommandArguments(Guid ConversationId, string Text);
public sealed record ShareTaskCommandArguments(Guid TaskId, Guid UserId, bool CanEdit);
public sealed record CreateCalendarEventCommandArguments(
    string Title,
    string? Purpose,
    string? Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyCollection<Guid>? ParticipantIds = null);
public sealed record UpdateCalendarEventCommandArguments(
    Guid Id,
    string Title,
    string? Purpose,
    string? Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyCollection<Guid>? ParticipantIds = null);

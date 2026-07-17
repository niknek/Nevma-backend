using Nevma.Commands.Api.Domain;
using Nevma.Contracts.Commands;

namespace Nevma.Commands.Api.Application;

public interface ICommandRepository
{
    Task AddAsync(CommandRequest command, CancellationToken cancellationToken = default);
    Task<CommandRequest?> GetAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<CommandRequest?> FindByKeyAsync(Guid userId, string key, CancellationToken cancellationToken = default);
}
public interface ICommandsUnitOfWork { Task<int> SaveChangesAsync(CancellationToken cancellationToken = default); }
public interface IIntentParser
{
    Task<ParseResult> ParseAsync(string transcript, DateTimeOffset now, CancellationToken cancellationToken = default);
}
public interface IAiProvider
{
    Task<AiInterpretationResult> InterpretAsync(
        string transcript,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
public interface IAiAssistantProvider
{
    Task<AssistantProviderResult> RespondAsync(
        AssistantRequest request,
        DateTimeOffset localNow,
        CancellationToken cancellationToken = default);
}
public interface ICommandValidator
{
    ParseResult Validate(ParsedCommand command, DateTimeOffset now);
}
public interface ISpeechToTextProvider
{
    Task<SpeechToTextResult> TranscribeAsync(
        Stream audio,
        string contentType,
        string? language,
        CancellationToken cancellationToken = default);
}
public interface ICommandExecutor
{
    Task<ExecutionResult> ExecuteAsync(CommandRequest command, string accessToken, CancellationToken cancellationToken = default);
    Task<bool> UndoAsync(CommandRequest command, string accessToken, CancellationToken cancellationToken = default);
}
public abstract record ParseResult
{
    public sealed record Parsed(ParsedCommand Command) : ParseResult;
    public sealed record Invalid(string Message) : ParseResult;
}
public sealed record ExecutionResult(bool Success, string? Resource, string? Error);
public abstract record AiInterpretationResult
{
    public sealed record Proposed(ParsedCommand Command) : AiInterpretationResult;
    public sealed record NotUnderstood(string Message) : AiInterpretationResult;
    public sealed record Unavailable : AiInterpretationResult;
}
public abstract record SpeechToTextResult
{
    public sealed record Transcribed(string Transcript, double? Confidence) : SpeechToTextResult;
    public sealed record Invalid(string Message) : SpeechToTextResult;
    public sealed record Unavailable : SpeechToTextResult;
}
public abstract record AssistantProviderResult
{
    public sealed record Completed(string Reply, string DetectedLanguage, ParsedCommand? Command) : AssistantProviderResult;
    public sealed record Unavailable(string Message) : AssistantProviderResult;
}

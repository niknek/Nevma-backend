using Nevma.Commands.Api.Domain;
using Nevma.Contracts.Commands;

namespace Nevma.Commands.Api.Application;

public sealed class CommandService(
    ICommandRepository repository,
    IIntentParser parser,
    ICommandExecutor executor,
    ICommandsUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<PreviewResult> PreviewAsync(
        Guid userId,
        PreviewCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
            return new PreviewResult.Invalid("idempotencyKey", "Idempotency key must contain 1 to 100 characters.");
        if (string.IsNullOrWhiteSpace(request.Transcript) || request.Transcript.Length > 2_000)
            return new PreviewResult.Invalid("transcript", "Transcript must contain 1 to 2000 characters.");
        var existing = await repository.FindByKeyAsync(userId, request.IdempotencyKey, cancellationToken);
        if (existing is not null)
            return new PreviewResult.Ready(ToResponse(existing), false);
        var parseResult = await parser.ParseAsync(
            request.Transcript,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (parseResult is not ParseResult.Parsed parsed)
            return new PreviewResult.Invalid("transcript", ((ParseResult.Invalid)parseResult).Message);

        var command = CommandRequest.Create(
            userId, request.IdempotencyKey, request.Transcript, parsed.Command, timeProvider.GetUtcNow());
        await repository.AddAsync(command, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new PreviewResult.Ready(ToResponse(command), true);
    }

    public async Task<CommandActionResult> ConfirmAsync(
        Guid id,
        Guid userId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var command = await repository.GetAsync(id, userId, cancellationToken);
        if (command is null) return new CommandActionResult.NotFound();
        if (!command.BeginExecution(timeProvider.GetUtcNow()))
            return new CommandActionResult.Conflict(ToResponse(command));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var result = await executor.ExecuteAsync(command, accessToken, cancellationToken);
        if (result.Success && result.Resource is not null)
            command.Complete(result.Resource, timeProvider.GetUtcNow());
        else
            command.Fail(result.Error ?? "Command execution failed.", timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CommandActionResult.Changed(ToResponse(command));
    }

    public async Task<CommandActionResult> UndoAsync(
        Guid id, Guid userId, string accessToken, CancellationToken cancellationToken = default)
    {
        var command = await repository.GetAsync(id, userId, cancellationToken);
        if (command is null) return new CommandActionResult.NotFound();
        if (command.Status != CommandStatus.Executed ||
            !await executor.UndoAsync(command, accessToken, cancellationToken) ||
            !command.MarkUndone(timeProvider.GetUtcNow()))
            return new CommandActionResult.Conflict(ToResponse(command));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CommandActionResult.Changed(ToResponse(command));
    }

    public async Task<CommandResponse?> GetAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var command = await repository.GetAsync(id, userId, cancellationToken);
        return command is null ? null : ToResponse(command);
    }

    internal static CommandResponse ToResponse(CommandRequest command) =>
        new(command.Id, command.Intent, command.Status, command.Summary, command.ArgumentsJson,
            true, command.ResultResource, command.Error, command.CreatedAt, command.ExecutedAt);
}

public abstract record PreviewResult
{
    public sealed record Ready(CommandResponse Command, bool IsNew) : PreviewResult;
    public sealed record Invalid(string Field, string Message) : PreviewResult;
}
public abstract record CommandActionResult
{
    public sealed record Changed(CommandResponse Command) : CommandActionResult;
    public sealed record Conflict(CommandResponse Command) : CommandActionResult;
    public sealed record NotFound : CommandActionResult;
}

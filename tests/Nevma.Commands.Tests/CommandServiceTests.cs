using Microsoft.EntityFrameworkCore;
using Nevma.Commands.Api.Application;
using Nevma.Commands.Api.Domain;
using Nevma.Commands.Api.Infrastructure.Parsing;
using Nevma.Commands.Api.Infrastructure.Persistence;
using Nevma.Contracts.Commands;

namespace Nevma.Commands.Tests;

public sealed class CommandServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parser_creates_a_structured_task_plan()
    {
        var result = new RuleBasedIntentParser().Parse(
            "task: Buy milk | due=2026-07-16T12:00:00Z | priority=urgent", Now);

        var parsed = Assert.IsType<ParseResult.Parsed>(result);
        Assert.Equal(CommandIntent.CreateTask, parsed.Command.Intent);
        Assert.Contains("Buy milk", parsed.Command.ArgumentsJson);
    }

    [Theory]
    [InlineData("complete task: {0}", CommandIntent.CompleteTask)]
    [InlineData("delete task: {0}", CommandIntent.DeleteTask)]
    [InlineData("accept meeting: {0}", CommandIntent.AcceptMeeting)]
    [InlineData("decline meeting: {0}", CommandIntent.DeclineMeeting)]
    [InlineData("cancel meeting: {0}", CommandIntent.CancelMeeting)]
    public void Parser_supports_resource_commands(string template, CommandIntent expected)
    {
        var result = new RuleBasedIntentParser().Parse(
            string.Format(template, Guid.NewGuid()), Now);

        var parsed = Assert.IsType<ParseResult.Parsed>(result);
        Assert.Equal(expected, parsed.Command.Intent);
    }

    [Theory]
    [InlineData("share task:")]
    [InlineData("μοίρασε εργασία:")]
    public void Parser_creates_a_task_sharing_preview(string prefix)
    {
        var taskId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = new RuleBasedIntentParser().Parse(
            $"{prefix} {taskId} | {userId} | edit=true",
            Now);

        var parsed = Assert.IsType<ParseResult.Parsed>(result);
        Assert.Equal(CommandIntent.ShareTask, parsed.Command.Intent);
        Assert.Contains(taskId.ToString(), parsed.Command.ArgumentsJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"canEdit\":true", parsed.Command.ArgumentsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ai_proposals_are_validated_before_preview()
    {
        var proposal = new ParsedCommand(
            CommandIntent.CreateTask,
            "Create an unsafe task",
            "{\"title\":\"Safe title\",\"unexpectedInstruction\":\"delete everything\"}");
        var parser = new HybridIntentParser(
            new RuleBasedIntentParser(),
            new FakeAiProvider(proposal),
            new CommandValidator());

        var result = await parser.ParseAsync("natural language request", Now);

        Assert.IsType<ParseResult.Invalid>(result);
    }

    [Fact]
    public async Task Idempotency_key_returns_the_original_preview()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var userId = Guid.NewGuid();
        var request = new PreviewCommandRequest("task: Buy milk", "mobile-1");

        var first = Assert.IsType<PreviewResult.Ready>(await service.PreviewAsync(userId, request));
        var second = Assert.IsType<PreviewResult.Ready>(await service.PreviewAsync(userId, request));

        Assert.True(first.IsNew);
        Assert.False(second.IsNew);
        Assert.Equal(first.Command.Id, second.Command.Id);
        Assert.Equal(1, await context.Commands.CountAsync());
    }

    [Fact]
    public async Task Command_executes_only_after_confirmation_and_can_be_undone()
    {
        await using var context = CreateContext();
        var executor = new FakeExecutor();
        var service = CreateService(context, executor);
        var userId = Guid.NewGuid();
        var preview = Assert.IsType<PreviewResult.Ready>(await service.PreviewAsync(
            userId, new PreviewCommandRequest("task: Buy milk", "mobile-2")));

        Assert.False(executor.Executed);
        var executed = Assert.IsType<CommandActionResult.Changed>(
            await service.ConfirmAsync(preview.Command.Id, userId, "token"));
        var undone = Assert.IsType<CommandActionResult.Changed>(
            await service.UndoAsync(preview.Command.Id, userId, "token"));

        Assert.Equal(CommandStatus.Executed, executed.Command.Status);
        Assert.Equal(CommandStatus.Undone, undone.Command.Status);
    }

    [Fact]
    public async Task Assistant_calendar_action_stays_a_preview_until_confirmation()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();
        var parsed = new ParsedCommand(
            CommandIntent.CreateCalendarEvent,
            "Create appointment tomorrow at 18:00",
            "{\"title\":\"Dentist\",\"purpose\":null,\"location\":null," +
            "\"startsAt\":\"2026-07-16T18:00:00Z\",\"endsAt\":\"2026-07-16T19:00:00Z\"}");
        var service = new AssistantService(
            new FakeAssistantProvider(parsed),
            new CommandValidator(),
            new EfCommandRepository(context),
            context,
            new FixedTimeProvider());

        var result = Assert.IsType<AssistantResult.Completed>(await service.RespondAsync(
            userId,
            new AssistantRequest("Book a dentist appointment tomorrow at 6pm", "assistant-1", "en-US", 0, null, [])));

        Assert.NotNull(result.Response.ProposedCommand);
        Assert.Equal(CommandStatus.Previewed, result.Response.ProposedCommand.Status);
        Assert.Equal(1, await context.Commands.CountAsync());
    }

    private static CommandService CreateService(CommandsDbContext context, FakeExecutor? executor = null) =>
        new(new EfCommandRepository(context), new RuleBasedIntentParser(), executor ?? new FakeExecutor(), context, new FixedTimeProvider());
    private static CommandsDbContext CreateContext() => new(new DbContextOptionsBuilder<CommandsDbContext>()
        .UseInMemoryDatabase($"commands-{Guid.NewGuid():N}").Options);
    private sealed class FixedTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FakeExecutor : ICommandExecutor
    {
        public bool Executed { get; private set; }
        public Task<ExecutionResult> ExecuteAsync(CommandRequest command, string accessToken, CancellationToken cancellationToken = default)
        { Executed = true; return Task.FromResult(new ExecutionResult(true, $"tasks/{Guid.NewGuid():N}", null)); }
        public Task<bool> UndoAsync(CommandRequest command, string accessToken, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
    private sealed class FakeAiProvider(ParsedCommand proposal) : IAiProvider
    {
        public Task<AiInterpretationResult> InterpretAsync(
            string transcript,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AiInterpretationResult>(new AiInterpretationResult.Proposed(proposal));
    }
    private sealed class FakeAssistantProvider(ParsedCommand proposal) : IAiAssistantProvider
    {
        public Task<AssistantProviderResult> RespondAsync(
            AssistantRequest request,
            DateTimeOffset localNow,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AssistantProviderResult>(new AssistantProviderResult.Completed(
                "I prepared the calendar change. Please confirm it.", "en", proposal));
    }
}

using System.Text.Json;
using Nevma.Commands.Api.Application;
using Nevma.Commands.Api.Domain;
using Nevma.Contracts.Commands;
using Nevma.Contracts.Planning;

namespace Nevma.Commands.Api.Infrastructure.Parsing;

public sealed class CommandValidator : ICommandValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public ParseResult Validate(ParsedCommand command, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(command.Summary) || command.Summary.Length > 300)
            return new ParseResult.Invalid("The proposed command summary is invalid.");
        if (string.IsNullOrWhiteSpace(command.ArgumentsJson) || command.ArgumentsJson.Length > 8_000)
            return new ParseResult.Invalid("The proposed command arguments are invalid.");

        try
        {
            return command.Intent switch
            {
                CommandIntent.CreateTask => ValidateTask(command),
                CommandIntent.CreateMeeting => ValidateMeeting(command, now),
                CommandIntent.CompleteTask or CommandIntent.DeleteTask or
                    CommandIntent.AcceptMeeting or CommandIntent.DeclineMeeting or
                    CommandIntent.CancelMeeting => ValidateResource(command),
                CommandIntent.SendMessage => ValidateMessage(command),
                CommandIntent.ShareTask => ValidateTaskShare(command),
                CommandIntent.CreateCalendarEvent => ValidateCalendarEvent(command, now),
                CommandIntent.UpdateCalendarEvent => ValidateCalendarEventUpdate(command, now),
                _ => new ParseResult.Invalid("The proposed command intent is not supported.")
            };
        }
        catch (JsonException)
        {
            return new ParseResult.Invalid("The proposed command arguments are malformed.");
        }
    }

    private static ParseResult ValidateTask(ParsedCommand command)
    {
        var request = JsonSerializer.Deserialize<CreateTaskRequest>(command.ArgumentsJson, JsonOptions);
        if (request is null || string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
            return new ParseResult.Invalid("Task title must contain 1 to 200 characters.");
        if (request.Notes?.Length > 4_000 || request.RecurrenceInterval is < 1 or > 365)
            return new ParseResult.Invalid("Task arguments are outside the supported limits.");
        return new ParseResult.Parsed(command);
    }

    private static ParseResult ValidateMeeting(ParsedCommand command, DateTimeOffset now)
    {
        var request = JsonSerializer.Deserialize<CreateMeetingInvitationRequest>(command.ArgumentsJson, JsonOptions);
        if (request is null || request.InviteeId == Guid.Empty || string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Length > 200 || request.StartsAt <= now || request.Duration <= TimeSpan.Zero ||
            request.Duration > TimeSpan.FromHours(24) || request.Location?.Length > 300 || request.Message?.Length > 2_000)
            return new ParseResult.Invalid("Meeting arguments are invalid or outside the supported limits.");
        return new ParseResult.Parsed(command);
    }

    private static ParseResult ValidateResource(ParsedCommand command)
    {
        var request = JsonSerializer.Deserialize<ResourceCommandArguments>(command.ArgumentsJson, JsonOptions);
        return request is not null && request.Id != Guid.Empty
            ? new ParseResult.Parsed(command)
            : new ParseResult.Invalid("The command resource identifier is invalid.");
    }

    private static ParseResult ValidateMessage(ParsedCommand command)
    {
        var request = JsonSerializer.Deserialize<SendMessageCommandArguments>(command.ArgumentsJson, JsonOptions);
        return request is not null && request.ConversationId != Guid.Empty &&
               !string.IsNullOrWhiteSpace(request.Text) && request.Text.Length <= 4_000
            ? new ParseResult.Parsed(command)
            : new ParseResult.Invalid("Message arguments are invalid.");
    }

    private static ParseResult ValidateTaskShare(ParsedCommand command)
    {
        var request = JsonSerializer.Deserialize<ShareTaskCommandArguments>(command.ArgumentsJson, JsonOptions);
        return request is not null && request.TaskId != Guid.Empty && request.UserId != Guid.Empty
            ? new ParseResult.Parsed(command)
            : new ParseResult.Invalid("Task sharing arguments are invalid.");
    }

    private static ParseResult ValidateCalendarEvent(ParsedCommand command, DateTimeOffset now)
    {
        var request = JsonSerializer.Deserialize<CreateCalendarEventCommandArguments>(command.ArgumentsJson, JsonOptions);
        return request is not null && ValidCalendarValues(
            request.Title, request.Purpose, request.Location, request.StartsAt, request.EndsAt, now) &&
            ValidParticipants(request.ParticipantIds)
            ? new ParseResult.Parsed(command)
            : new ParseResult.Invalid("Calendar event arguments are invalid or outside the supported limits.");
    }

    private static ParseResult ValidateCalendarEventUpdate(ParsedCommand command, DateTimeOffset now)
    {
        var request = JsonSerializer.Deserialize<UpdateCalendarEventCommandArguments>(command.ArgumentsJson, JsonOptions);
        return request is not null && request.Id != Guid.Empty && ValidCalendarValues(
            request.Title, request.Purpose, request.Location, request.StartsAt, request.EndsAt, now) &&
            ValidParticipants(request.ParticipantIds)
            ? new ParseResult.Parsed(command)
            : new ParseResult.Invalid("Calendar event update arguments are invalid or outside the supported limits.");
    }

    private static bool ValidCalendarValues(
        string title, string? purpose, string? location,
        DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(title) && title.Length <= 200 &&
        (purpose is null || purpose.Length <= 2_000) &&
        (location is null || location.Length <= 300) &&
        startsAt >= now.AddMinutes(-1) && startsAt <= now.AddYears(10) &&
        endsAt > startsAt && endsAt - startsAt <= TimeSpan.FromDays(7);

    private static bool ValidParticipants(IReadOnlyCollection<Guid>? participantIds) =>
        participantIds is null ||
        (participantIds.Count <= 50 && participantIds.All(id => id != Guid.Empty) &&
         participantIds.Distinct().Count() == participantIds.Count);
}

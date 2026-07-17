using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Nevma.Commands.Api.Application;
using Nevma.Commands.Api.Domain;
using Nevma.Contracts.Commands;
using Nevma.ServiceDefaults.Extensions;

namespace Nevma.Commands.Api.Infrastructure.Execution;

public sealed class BackendCommandExecutor(IHttpClientFactory clientFactory) : ICommandExecutor
{
    public async Task<ExecutionResult> ExecuteAsync(
        CommandRequest command,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var target = CreateTarget(command);
        if (target is null)
            return new ExecutionResult(false, null, "The command intent is not supported.");

        using var request = target.Request;
        AddUserCredentials(request, command, accessToken);
        if (target.IncludeArguments)
            request.Content = new StringContent(command.ArgumentsJson, Encoding.UTF8, "application/json");

        var client = clientFactory.CreateClient(target.ClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception.IsTransientHttpFailure(cancellationToken))
        {
            return new ExecutionResult(false, null, $"{target.ClientName} is temporarily unavailable.");
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return new ExecutionResult(
                    false,
                    null,
                    $"{target.ClientName} rejected the command with status {(int)response.StatusCode}.");

            if (command.Intent is CommandIntent.DeleteTask or CommandIntent.ShareTask)
                return new ExecutionResult(true, target.ResourcePrefix, null);

            if (response.Content.Headers.ContentLength == 0)
                return new ExecutionResult(true, target.ResourcePrefix, null);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("id", out var id) || !id.TryGetGuid(out var resourceId))
                return new ExecutionResult(false, null, $"{target.ClientName} response did not contain a resource identifier.");

            var resource = command.Intent == CommandIntent.SendMessage
                ? $"{target.ResourcePrefix}/{resourceId:N}"
                : command.Intent is CommandIntent.CompleteTask or CommandIntent.AcceptMeeting or
                    CommandIntent.DeclineMeeting or CommandIntent.CancelMeeting
                    ? target.ResourcePrefix
                    : $"{target.ResourcePrefix}/{resourceId:N}";
            return new ExecutionResult(true, resource, null);
        }
    }

    public async Task<bool> UndoAsync(
        CommandRequest command,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.ResultResource)) return false;

        var undo = command.Intent switch
        {
            CommandIntent.CreateTask => new UndoTarget("Planning", HttpMethod.Delete, $"/api/{command.ResultResource}"),
            CommandIntent.CreateMeeting => new UndoTarget("Planning", HttpMethod.Post, $"/api/{command.ResultResource}/cancel"),
            CommandIntent.SendMessage => new UndoTarget("Messaging", HttpMethod.Delete, $"/api/{command.ResultResource}"),
            CommandIntent.ShareTask => new UndoTarget("Planning", HttpMethod.Delete, $"/api/{command.ResultResource}"),
            _ => null
        };
        if (undo is null) return false;

        using var request = new HttpRequestMessage(undo.Method, undo.Path);
        AddUserCredentials(request, command, accessToken);
        try
        {
            using var response = await clientFactory.CreateClient(undo.ClientName).SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception.IsTransientHttpFailure(cancellationToken))
        {
            return false;
        }
    }

    private static CommandTarget? CreateTarget(CommandRequest command)
    {
        ResourceCommandArguments? resource = null;
        SendMessageCommandArguments? message = null;
        ShareTaskCommandArguments? share = null;
        if (command.Intent is CommandIntent.CompleteTask or CommandIntent.DeleteTask or
            CommandIntent.AcceptMeeting or CommandIntent.DeclineMeeting or CommandIntent.CancelMeeting)
            resource = JsonSerializer.Deserialize<ResourceCommandArguments>(command.ArgumentsJson);
        if (command.Intent == CommandIntent.SendMessage)
            message = JsonSerializer.Deserialize<SendMessageCommandArguments>(command.ArgumentsJson);
        if (command.Intent == CommandIntent.ShareTask)
            share = JsonSerializer.Deserialize<ShareTaskCommandArguments>(command.ArgumentsJson);
        UpdateCalendarEventCommandArguments? calendarUpdate = null;
        if (command.Intent == CommandIntent.UpdateCalendarEvent)
            calendarUpdate = JsonSerializer.Deserialize<UpdateCalendarEventCommandArguments>(command.ArgumentsJson);

        return command.Intent switch
        {
            CommandIntent.CreateTask => new("Planning", new(HttpMethod.Post, "/api/tasks"), true, "tasks"),
            CommandIntent.CompleteTask when resource is not null => new(
                "Planning", new(HttpMethod.Post, $"/api/tasks/{resource.Id}/complete"), false, $"tasks/{resource.Id:N}"),
            CommandIntent.DeleteTask when resource is not null => new(
                "Planning", new(HttpMethod.Delete, $"/api/tasks/{resource.Id}"), false, $"tasks/{resource.Id:N}"),
            CommandIntent.CreateMeeting => new(
                "Planning", new(HttpMethod.Post, "/api/meeting-invitations"), true, "meeting-invitations"),
            CommandIntent.AcceptMeeting when resource is not null => MeetingAction(resource.Id, "accept"),
            CommandIntent.DeclineMeeting when resource is not null => MeetingAction(resource.Id, "decline"),
            CommandIntent.CancelMeeting when resource is not null => MeetingAction(resource.Id, "cancel"),
            CommandIntent.SendMessage when message is not null => new(
                "Messaging",
                new(HttpMethod.Post, $"/api/conversations/{message.ConversationId}/messages"),
                true,
                $"conversations/{message.ConversationId:N}/messages"),
            CommandIntent.ShareTask when share is not null => new(
                "Planning",
                new(HttpMethod.Post, $"/api/tasks/{share.TaskId}/collaborators"),
                true,
                $"tasks/{share.TaskId:N}/collaborators/{share.UserId:N}"),
            CommandIntent.CreateCalendarEvent => new(
                "Planning", new(HttpMethod.Post, "/api/personal-calendar/"), true, "personal-calendar"),
            CommandIntent.UpdateCalendarEvent when calendarUpdate is not null => new(
                "Planning", new(HttpMethod.Put, $"/api/personal-calendar/{calendarUpdate.Id}"), true,
                $"personal-calendar/{calendarUpdate.Id:N}"),
            _ => null
        };
    }

    private static void AddUserCredentials(HttpRequestMessage request, CommandRequest command, string accessToken)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        else
            request.Headers.Add("X-Nevma-User-Id", command.UserId.ToString());
    }

    private static CommandTarget MeetingAction(Guid id, string action) => new(
        "Planning",
        new(HttpMethod.Post, $"/api/meeting-invitations/{id}/{action}"),
        false,
        $"meeting-invitations/{id:N}");

    private sealed record CommandTarget(
        string ClientName,
        HttpRequestMessage Request,
        bool IncludeArguments,
        string ResourcePrefix);
    private sealed record UndoTarget(string ClientName, HttpMethod Method, string Path);
}

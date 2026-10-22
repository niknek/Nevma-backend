using System.Text.Json;
using System.Text.Json.Serialization;
using Nevma.Contracts.Integration;
using Nevma.Planning.Api.Application;
using Nevma.Planning.Api.Infrastructure.Persistence;

namespace Nevma.Planning.Api.Infrastructure.Outbox;

public sealed class EfPlanningEventOutbox(PlanningDbContext dbContext) : IPlanningEventOutbox
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public void Add(MeetingInvitationChangedIntegrationEvent integrationEvent)
    {
        var payload = JsonSerializer.Serialize(integrationEvent, SerializerOptions);
        dbContext.OutboxMessages.Add(OutboxMessage.Create(
            integrationEvent.EventId,
            PlanningIntegrationEventTypes.MeetingInvitationChanged,
            payload,
            integrationEvent.OccurredAt));
    }

    public void Add(TaskReminderDueIntegrationEvent integrationEvent)
    {
        var payload = JsonSerializer.Serialize(integrationEvent, SerializerOptions);
        dbContext.OutboxMessages.Add(OutboxMessage.Create(
            integrationEvent.EventId,
            PlanningIntegrationEventTypes.TaskReminderDue,
            payload,
            integrationEvent.OccurredAt));
    }

    public void Add(CalendarEventReminderDueIntegrationEvent integrationEvent)
    {
        var payload = JsonSerializer.Serialize(integrationEvent, SerializerOptions);
        dbContext.OutboxMessages.Add(OutboxMessage.Create(
            integrationEvent.EventId,
            PlanningIntegrationEventTypes.CalendarEventReminderDue,
            payload,
            integrationEvent.OccurredAt));
    }
}

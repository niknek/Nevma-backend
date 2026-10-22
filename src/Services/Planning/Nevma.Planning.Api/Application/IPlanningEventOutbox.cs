using Nevma.Contracts.Integration;

namespace Nevma.Planning.Api.Application;

public interface IPlanningEventOutbox
{
    void Add(MeetingInvitationChangedIntegrationEvent integrationEvent);
    void Add(TaskReminderDueIntegrationEvent integrationEvent);
    void Add(CalendarEventReminderDueIntegrationEvent integrationEvent);
    void Add(CalendarEventExpiredIntegrationEvent integrationEvent);
}

using Nevma.Contracts.Integration;
using Nevma.Contracts.Planning;
using Nevma.Notifications.Api.Application.Delivery;
using Nevma.Notifications.Api.Application.Notifications;
using Nevma.Notifications.Api.Application.PushDevices;
using Nevma.Notifications.Api.Domain.Delivery;
using Nevma.Notifications.Api.Domain.Notifications;

namespace Nevma.Notifications.Api.Application.Integration;

public sealed class PlanningEventHandler(
    INotificationRepository notificationRepository,
    IPushDeviceRepository pushDeviceRepository,
    IDeliveryAttemptRepository deliveryAttemptRepository,
    IIntegrationEventInbox inbox,
    INotificationsUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILiveNotificationPublisher livePublisher,
    ILogger<PlanningEventHandler> logger)
{
    public async Task<PlanningEventHandleResult> HandleAsync(
        MeetingInvitationChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        if (await inbox.ExistsAsync(integrationEvent.EventId, cancellationToken))
            return PlanningEventHandleResult.AlreadyProcessed;
        if (integrationEvent.OrganizerId == integrationEvent.InviteeId)
            throw new InvalidDataException("Meeting participants must be different users.");

        var now = timeProvider.GetUtcNow();
        var (title, body) = GetPrivacySafeContent(integrationEvent);
        var created = new List<Notification>();
        var recipients = integrationEvent.Status == MeetingInvitationStatus.Pending
            ? new[] { integrationEvent.InviteeId }
            : new[] { integrationEvent.OrganizerId, integrationEvent.InviteeId };
        foreach (var userId in recipients.Distinct())
        {
            var notification = Notification.Create(
                userId,
                "meeting-invitation.changed",
                title,
                body,
                now,
                integrationEvent.CalendarEventId);
            await notificationRepository.AddAsync(notification, cancellationToken);
            created.Add(notification);
            var devices = await pushDeviceRepository.ListActiveAsync(userId, cancellationToken);
            foreach (var device in devices)
            {
                await deliveryAttemptRepository.AddAsync(
                    DeliveryAttempt.Create(notification.Id, device.Id, now),
                    cancellationToken);
            }
        }

        inbox.Add(
            integrationEvent.EventId,
            PlanningIntegrationEventTypes.MeetingInvitationChanged,
            now);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateNotificationEventException)
        {
            return PlanningEventHandleResult.AlreadyProcessed;
        }

        foreach (var notification in created)
            await PublishBestEffortAsync(notification, cancellationToken);
        return PlanningEventHandleResult.Processed;
    }

    public async Task<PlanningEventHandleResult> HandleAsync(
        TaskReminderDueIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        if (await inbox.ExistsAsync(integrationEvent.EventId, cancellationToken))
            return PlanningEventHandleResult.AlreadyProcessed;
        if (integrationEvent.OwnerId == Guid.Empty || integrationEvent.TaskId == Guid.Empty)
            throw new InvalidDataException("Task reminder identifiers are required.");

        var now = timeProvider.GetUtcNow();
        var notification = Notification.Create(
            integrationEvent.OwnerId,
            "task.reminder",
            "Task reminder",
            "Open Nevma to review what is due.",
            now);
        await notificationRepository.AddAsync(notification, cancellationToken);
        var devices = await pushDeviceRepository.ListActiveAsync(integrationEvent.OwnerId, cancellationToken);
        foreach (var device in devices)
        {
            await deliveryAttemptRepository.AddAsync(
                DeliveryAttempt.Create(notification.Id, device.Id, now),
                cancellationToken);
        }

        inbox.Add(integrationEvent.EventId, PlanningIntegrationEventTypes.TaskReminderDue, now);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateNotificationEventException)
        {
            return PlanningEventHandleResult.AlreadyProcessed;
        }

        await PublishBestEffortAsync(notification, cancellationToken);
        return PlanningEventHandleResult.Processed;
    }

    public async Task<PlanningEventHandleResult> HandleAsync(
        CalendarEventReminderDueIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        if (await inbox.ExistsAsync(integrationEvent.EventId, cancellationToken))
            return PlanningEventHandleResult.AlreadyProcessed;
        if (integrationEvent.CalendarEventId == Guid.Empty || integrationEvent.UserIds.Count == 0)
            throw new InvalidDataException("Calendar reminder identifiers are required.");

        var now = timeProvider.GetUtcNow();
        var created = new List<Notification>();
        foreach (var userId in integrationEvent.UserIds.Where(id => id != Guid.Empty).Distinct())
        {
            var notification = Notification.Create(
                userId,
                "calendar.reminder",
                "Appointment coming up",
                $"{integrationEvent.Title} starts soon.",
                now,
                integrationEvent.CalendarEventId);
            await notificationRepository.AddAsync(notification, cancellationToken);
            created.Add(notification);
            var devices = await pushDeviceRepository.ListActiveAsync(userId, cancellationToken);
            foreach (var device in devices)
                await deliveryAttemptRepository.AddAsync(DeliveryAttempt.Create(notification.Id, device.Id, now), cancellationToken);
        }

        inbox.Add(integrationEvent.EventId, PlanningIntegrationEventTypes.CalendarEventReminderDue, now);
        try { await unitOfWork.SaveChangesAsync(cancellationToken); }
        catch (DuplicateNotificationEventException) { return PlanningEventHandleResult.AlreadyProcessed; }
        foreach (var notification in created)
            await PublishBestEffortAsync(notification, cancellationToken);
        return PlanningEventHandleResult.Processed;
    }

    public async Task<PlanningEventHandleResult> HandleAsync(
        CalendarEventExpiredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        if (await inbox.ExistsAsync(integrationEvent.EventId, cancellationToken))
            return PlanningEventHandleResult.AlreadyProcessed;
        if (integrationEvent.CalendarEventId == Guid.Empty || integrationEvent.UserIds.Count == 0)
            throw new InvalidDataException("Expired calendar event identifiers are required.");

        var now = timeProvider.GetUtcNow();
        var created = new List<Notification>();
        foreach (var userId in integrationEvent.UserIds.Where(id => id != Guid.Empty).Distinct())
        {
            var notification = Notification.Create(
                userId,
                "calendar.cancelled",
                "Appointment cancelled",
                $"{integrationEvent.Title} was cancelled. Reason: {integrationEvent.Reason}",
                now,
                integrationEvent.CalendarEventId);
            await notificationRepository.AddAsync(notification, cancellationToken);
            created.Add(notification);
            var devices = await pushDeviceRepository.ListActiveAsync(userId, cancellationToken);
            foreach (var device in devices)
                await deliveryAttemptRepository.AddAsync(DeliveryAttempt.Create(notification.Id, device.Id, now), cancellationToken);
        }

        inbox.Add(integrationEvent.EventId, PlanningIntegrationEventTypes.CalendarEventExpired, now);
        try { await unitOfWork.SaveChangesAsync(cancellationToken); }
        catch (DuplicateNotificationEventException) { return PlanningEventHandleResult.AlreadyProcessed; }
        foreach (var notification in created)
            await PublishBestEffortAsync(notification, cancellationToken);
        return PlanningEventHandleResult.Processed;
    }

    private async Task PublishBestEffortAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            await livePublisher.PublishAsync(
                notification.UserId,
                new(
                    notification.Id,
                    notification.Type,
                    notification.Title,
                    notification.Body,
                    notification.CreatedAt,
                    notification.ReadAt,
                    notification.ResourceId),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Live publication failed for notification {NotificationId}; durable delivery remains queued.",
                notification.Id);
        }
    }

    private static (string Title, string Body) GetPrivacySafeContent(MeetingInvitationChangedIntegrationEvent integrationEvent) => integrationEvent.Status switch
    {
        MeetingInvitationStatus.Pending => ("New meeting invitation", "Do you accept this meeting? Open Nevma to respond."),
        MeetingInvitationStatus.Accepted => ("Meeting accepted", integrationEvent.PendingCount > 0
            ? $"A participant accepted the meeting. {integrationEvent.PendingCount} participant(s) have not responded yet."
            : "A participant accepted the meeting."),
        MeetingInvitationStatus.Declined => ("Meeting declined", string.IsNullOrWhiteSpace(integrationEvent.ResponseReason)
            ? "A participant declined the meeting."
            : $"A participant declined the meeting. Reason: {integrationEvent.ResponseReason}"),
        MeetingInvitationStatus.CounterProposed => ("Meeting change proposed", "Open Nevma to review the proposed change."),
        MeetingInvitationStatus.RescheduleProposed => ("Meeting change proposed", "Open Nevma to review the proposed change."),
        MeetingInvitationStatus.Cancelled => ("Meeting cancelled", "Open Nevma to review this update."),
        _ => ("Meeting update", "Open Nevma to review this update.")
    };
}

public enum PlanningEventHandleResult
{
    Processed,
    AlreadyProcessed
}

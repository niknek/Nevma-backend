using Nevma.Contracts.Notifications;
using Nevma.Notifications.Api.Domain.Notifications;
using Nevma.Notifications.Api.Application.Delivery;
using Nevma.Notifications.Api.Application.PushDevices;
using Nevma.Notifications.Api.Domain.Delivery;

namespace Nevma.Notifications.Api.Application.Notifications;

public sealed class NotificationService(
    INotificationRepository repository,
    INotificationsUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IPushDeviceRepository? pushDevices = null,
    IDeliveryAttemptRepository? deliveryAttempts = null)
{
    public async Task<IReadOnlyList<NotificationResponse>> ListAsync(
        Guid userId,
        int requestedLimit,
        CancellationToken cancellationToken = default) =>
        (await repository.ListAsync(userId, Math.Clamp(requestedLimit, 1, 100), cancellationToken))
            .Select(ToResponse)
            .ToArray();

    public async Task<MarkNotificationReadResult> MarkReadAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var notification = await repository.GetAsync(id, userId, cancellationToken);
        if (notification is null)
            return new MarkNotificationReadResult.NotFound();

        notification.MarkRead(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MarkNotificationReadResult.Read(ToResponse(notification));
    }

    public async Task<NotificationResponse> CreateAsync(
        Guid userId,
        string type,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty ||
            string.IsNullOrWhiteSpace(type) || type.Length > 100 ||
            string.IsNullOrWhiteSpace(title) || title.Length > 200 ||
            string.IsNullOrWhiteSpace(body) || body.Length > 4_000)
        {
            throw new InvalidDataException("Notification data is invalid.");
        }

        var notification = Notification.Create(
            userId,
            type,
            title,
            body,
            timeProvider.GetUtcNow());
        await repository.AddAsync(notification, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(notification);
    }

    public async Task<NotificationResponse> CreatePushAsync(Guid userId, string type, string title,
        string body, Guid resourceId, CancellationToken cancellationToken = default)
    {
        if (pushDevices is null || deliveryAttempts is null)
            throw new InvalidOperationException("Push delivery services are not configured.");
        var now = timeProvider.GetUtcNow();
        var notification = Notification.Create(userId, type, title, body, now, resourceId);
        await repository.AddAsync(notification, cancellationToken);
        foreach (var device in await pushDevices.ListActiveAsync(userId, cancellationToken))
            await deliveryAttempts.AddAsync(DeliveryAttempt.Create(notification.Id, device.Id, now), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(notification);
    }

    private static NotificationResponse ToResponse(Notification notification) =>
        new(
            notification.Id,
            notification.Type,
            notification.Title,
            notification.Body,
            notification.CreatedAt,
            notification.ReadAt,
            notification.ResourceId);
}

public abstract record MarkNotificationReadResult
{
    public sealed record Read(NotificationResponse Notification) : MarkNotificationReadResult;
    public sealed record NotFound : MarkNotificationReadResult;
}

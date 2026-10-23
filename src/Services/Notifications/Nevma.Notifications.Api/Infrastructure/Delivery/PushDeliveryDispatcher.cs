using Nevma.Notifications.Api.Application.Delivery;
using Nevma.Notifications.Api.Application.PushDevices;
using Nevma.Notifications.Api.Application.Notifications;
using Nevma.Notifications.Api.Domain.Notifications;

namespace Nevma.Notifications.Api.Infrastructure.Delivery;

public sealed class PushDeliveryDispatcher(
    IServiceScopeFactory scopeFactory,
    IPushNotificationSender sender,
    TimeProvider timeProvider,
    ILogger<PushDeliveryDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processedAny = false;
            try
            {
                processedAny = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Push delivery batch processing failed.");
            }

            await Task.Delay(
                processedAny ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(1),
                stoppingToken);
        }
    }

    private async Task<bool> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<DeliveryAttemptStore>();
        var tokenProtector = scope.ServiceProvider.GetRequiredService<IPushTokenProtector>();
        var preferenceRepository = scope.ServiceProvider.GetRequiredService<INotificationPreferenceRepository>();
        var workItems = await store.ClaimBatchAsync(
            timeProvider.GetUtcNow(),
            batchSize: 20,
            cancellationToken);

        foreach (var workItem in workItems)
        {
            var now = timeProvider.GetUtcNow();
            try
            {
                var preference = await preferenceRepository.GetAsync(
                    workItem.Notification.UserId,
                    cancellationToken);
                var deliveryPreference = preference?.Evaluate(workItem.Notification.Type, now)
                    ?? DeliveryPreference.Allowed;
                if (deliveryPreference is DeliveryPreference.Disable)
                {
                    workItem.Attempt.MarkPermanentFailure("PreferenceDisabled", now);
                }
                else if (deliveryPreference is DeliveryPreference.Deferred deferred)
                {
                    workItem.Attempt.Defer(deferred.Until);
                }
                else if (!workItem.Device.IsActive)
                {
                    workItem.Attempt.MarkPermanentFailure("DeviceRevoked", now);
                }
                else
                {
                    var target = tokenProtector.Unprotect(workItem.Device.ProtectedToken);
                    var result = await sender.SendAsync(
                        (Nevma.Contracts.Notifications.PushPlatform)(int)workItem.Device.Platform,
                        target,
                        workItem.Notification.Title,
                        workItem.Notification.Body,
                        new Dictionary<string, string>
                        {
                            ["notificationId"] = workItem.Notification.Id.ToString("N"),
                            ["type"] = workItem.Notification.Type
                        },
                        cancellationToken);
                    ApplyResult(workItem, result, now);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                workItem.Attempt.MarkRetryableFailure(exception.GetType().Name, now);
                logger.LogWarning(
                    exception,
                    "Push delivery attempt {AttemptId} failed and will be retried.",
                    workItem.Attempt.Id);
            }

            await store.SaveChangesAsync(cancellationToken);
        }
        return workItems.Count > 0;
    }

    private static void ApplyResult(
        DeliveryWorkItem workItem,
        PushSendResult result,
        DateTimeOffset now)
    {
        switch (result)
        {
            case PushSendResult.Sent sent:
                workItem.Attempt.MarkSent(sent.ProviderMessageId, now);
                break;
            case PushSendResult.PermanentFailure permanent:
                workItem.Attempt.MarkPermanentFailure(permanent.ErrorCode, now);
                workItem.Device.Revoke();
                break;
            case PushSendResult.RetryableFailure retryable:
                workItem.Attempt.MarkRetryableFailure(retryable.ErrorCode, now);
                break;
        }
    }
}

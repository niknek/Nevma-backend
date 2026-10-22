namespace Nevma.Planning.Api.Infrastructure.Tasks;

public sealed class CalendarReminderWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<CalendarReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<CalendarReminderProcessor>();
                var processed = await processor.ProcessDueAsync(cancellationToken: stoppingToken);
                if (processed > 0)
                    logger.LogInformation("Queued {ReminderCount} calendar reminders.", processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Calendar reminder scan failed; it will be retried.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

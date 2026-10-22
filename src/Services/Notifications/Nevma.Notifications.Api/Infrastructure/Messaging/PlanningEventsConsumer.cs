using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Nevma.Contracts.Integration;
using Nevma.Notifications.Api.Application.Integration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Nevma.Notifications.Api.Infrastructure.Messaging;

public sealed class PlanningEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<MessageBrokerOptions> options,
    ILogger<PlanningEventsConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly MessageBrokerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Uri))
            throw new InvalidOperationException("MessageBroker:Uri is required when the broker is enabled.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification event consumer disconnected; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(_options.Uri!, UriKind.Absolute),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            ConsumerDispatchConcurrency = 1
        };
        await using var connection = await factory.CreateConnectionAsync(
            "nevma-notifications-planning-events",
            cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await ConfigureTopologyAsync(channel, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
            await HandleDeliveryAsync(channel, delivery, cancellationToken);
        await channel.BasicConsumeAsync(
            _options.Queue,
            autoAck: false,
            consumer,
            cancellationToken);
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private async Task ConfigureTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        var deadLetterExchange = $"{_options.Exchange}.dead-letter";
        var deadLetterQueue = $"{_options.Queue}.dead-letter";
        await channel.ExchangeDeclareAsync(
            _options.Exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            deadLetterExchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            deadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            deadLetterQueue,
            deadLetterExchange,
            "#",
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            _options.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = deadLetterExchange
            },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            _options.Queue,
            _options.Exchange,
            PlanningIntegrationEventTypes.MeetingInvitationChanged,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            _options.Queue,
            _options.Exchange,
            PlanningIntegrationEventTypes.TaskReminderDue,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            _options.Queue,
            _options.Exchange,
            PlanningIntegrationEventTypes.CalendarEventReminderDue,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            _options.Queue,
            _options.Exchange,
            PlanningIntegrationEventTypes.CalendarEventExpired,
            cancellationToken: cancellationToken);
        await channel.BasicQosAsync(0, 10, false, cancellationToken);
    }

    private async Task HandleDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<PlanningEventHandler>();
            var eventId = delivery.BasicProperties.Type switch
            {
                PlanningIntegrationEventTypes.MeetingInvitationChanged =>
                    await HandleMeetingAsync(handler, delivery.Body.ToArray(), cancellationToken),
                PlanningIntegrationEventTypes.TaskReminderDue =>
                    await HandleTaskReminderAsync(handler, delivery.Body.ToArray(), cancellationToken),
                PlanningIntegrationEventTypes.CalendarEventReminderDue =>
                    await HandleCalendarReminderAsync(handler, delivery.Body.ToArray(), cancellationToken),
                PlanningIntegrationEventTypes.CalendarEventExpired =>
                    await HandleCalendarExpiredAsync(handler, delivery.Body.ToArray(), cancellationToken),
                _ => throw new InvalidDataException("Unsupported integration event type.")
            };
            if (!string.Equals(
                delivery.BasicProperties.MessageId,
                eventId.ToString("N"),
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Integration event identifier does not match its envelope.");
            await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            logger.LogWarning(
                exception,
                "Invalid notification event {MessageId} was dead-lettered.",
                delivery.BasicProperties.MessageId);
            await channel.BasicRejectAsync(delivery.DeliveryTag, false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Notification event {MessageId} failed and will be retried.",
                delivery.BasicProperties.MessageId);
            await channel.BasicNackAsync(delivery.DeliveryTag, false, true, cancellationToken);
        }
    }

    private static async Task<Guid> HandleMeetingAsync(
        PlanningEventHandler handler,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var integrationEvent = JsonSerializer.Deserialize<MeetingInvitationChangedIntegrationEvent>(
            body,
            SerializerOptions) ?? throw new InvalidDataException("Integration event payload is empty.");
        await handler.HandleAsync(integrationEvent, cancellationToken);
        return integrationEvent.EventId;
    }

    private static async Task<Guid> HandleTaskReminderAsync(
        PlanningEventHandler handler,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var integrationEvent = JsonSerializer.Deserialize<TaskReminderDueIntegrationEvent>(
            body,
            SerializerOptions) ?? throw new InvalidDataException("Integration event payload is empty.");
        await handler.HandleAsync(integrationEvent, cancellationToken);
        return integrationEvent.EventId;
    }

    private static async Task<Guid> HandleCalendarReminderAsync(
        PlanningEventHandler handler,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var integrationEvent = JsonSerializer.Deserialize<CalendarEventReminderDueIntegrationEvent>(
            body,
            SerializerOptions) ?? throw new InvalidDataException("Integration event payload is empty.");
        await handler.HandleAsync(integrationEvent, cancellationToken);
        return integrationEvent.EventId;
    }

    private static async Task<Guid> HandleCalendarExpiredAsync(
        PlanningEventHandler handler,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var integrationEvent = JsonSerializer.Deserialize<CalendarEventExpiredIntegrationEvent>(
            body,
            SerializerOptions) ?? throw new InvalidDataException("Integration event payload is empty.");
        await handler.HandleAsync(integrationEvent, cancellationToken);
        return integrationEvent.EventId;
    }
}

using Microsoft.EntityFrameworkCore;
using Nevma.Notifications.Api.Application;
using Nevma.Notifications.Api.Application.Notifications;
using Nevma.Notifications.Api.Application.Integration;
using Nevma.Notifications.Api.Application.Delivery;
using Nevma.Notifications.Api.Application.PushDevices;
using Nevma.Notifications.Api.Infrastructure.Authentication;
using Nevma.Notifications.Api.Infrastructure.Notifications;
using Nevma.Notifications.Api.Infrastructure.Inbox;
using Nevma.Notifications.Api.Infrastructure.Delivery;
using Nevma.Notifications.Api.Infrastructure.Messaging;
using Nevma.Notifications.Api.Infrastructure.Persistence;
using Nevma.Notifications.Api.Infrastructure.PushDevices;
using Nevma.Notifications.Api.Infrastructure.Realtime;
using StackExchange.Redis;
using Nevma.ServiceDefaults.Extensions;

namespace Nevma.Notifications.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationsService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddNevmaDataProtection(configuration, "Nevma.Notifications");
        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("NotificationsDatabase"),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "notifications");
                    npgsqlOptions.EnableRetryOnFailure();
                }));
        services.AddNotificationsAuthentication(configuration);
        var signalR = services.AddSignalR(options => options.MaximumReceiveMessageSize = 16 * 1024);
        var realtimeOptions = configuration
            .GetSection(NotificationRealtimeOptions.SectionName)
            .Get<NotificationRealtimeOptions>() ?? new NotificationRealtimeOptions();
        if (realtimeOptions.Enabled)
        {
            if (string.IsNullOrWhiteSpace(realtimeOptions.ConnectionString))
                throw new InvalidOperationException("Redis:ConnectionString is required when Redis is enabled.");
            signalR.AddStackExchangeRedis(realtimeOptions.ConnectionString, options =>
            {
                options.Configuration.AbortOnConnectFail = false;
                options.Configuration.ChannelPrefix = RedisChannel.Literal("nevma:notifications");
            });
        }
        services.AddScoped<INotificationsUnitOfWork>(provider =>
            provider.GetRequiredService<NotificationsDbContext>());
        services.AddScoped<INotificationRepository, EfNotificationRepository>();
        services.AddScoped<INotificationPreferenceRepository, EfNotificationPreferenceRepository>();
        services.AddScoped<IIntegrationEventInbox, EfIntegrationEventInbox>();
        services.AddScoped<IDeliveryAttemptRepository, EfDeliveryAttemptRepository>();
        services.AddScoped<DeliveryAttemptStore>();
        services.AddScoped<IPushDeviceRepository, EfPushDeviceRepository>();
        services.AddSingleton<IPushTokenProtector, DataProtectionPushTokenProtector>();
        services.AddScoped<NotificationService>();
        services.AddScoped<NotificationPreferenceService>();
        services.AddScoped<ILiveNotificationPublisher, SignalRLiveNotificationPublisher>();
        services.AddScoped<PushDeviceService>();
        services.AddScoped<PlanningEventHandler>();

        var brokerOptions = configuration
            .GetSection(MessageBrokerOptions.SectionName)
            .Get<MessageBrokerOptions>() ?? new MessageBrokerOptions();
        services.Configure<MessageBrokerOptions>(
            configuration.GetSection(MessageBrokerOptions.SectionName));
        if (brokerOptions.Enabled)
            services.AddHostedService<PlanningEventsConsumer>();

        var pushOptions = configuration
            .GetSection(PushDeliveryOptions.SectionName)
            .Get<PushDeliveryOptions>() ?? new PushDeliveryOptions();
        services.Configure<PushDeliveryOptions>(
            configuration.GetSection(PushDeliveryOptions.SectionName));
        if (pushOptions.Enabled)
        {
            services.AddSingleton<IPushNotificationSender, MultiPlatformPushNotificationSender>();
            services.AddHostedService<PushDeliveryDispatcher>();
        }
        return services;
    }
}

using Microsoft.EntityFrameworkCore;
using Nevma.Messaging.Api.Application;
using Nevma.Messaging.Api.Application.Conversations;
using Nevma.Messaging.Api.Application.Messages;
using Nevma.Messaging.Api.Application.Integration;
using Nevma.Messaging.Api.Infrastructure.Authentication;
using Nevma.Messaging.Api.Infrastructure.Conversations;
using Nevma.Messaging.Api.Infrastructure.Messages;
using Nevma.Messaging.Api.Infrastructure.Persistence;
using Nevma.Messaging.Api.Infrastructure.Inbox;
using Nevma.Messaging.Api.Infrastructure.Messaging;
using Nevma.Messaging.Api.Infrastructure.Realtime;
using Nevma.Messaging.Api.Application.Presence;
using StackExchange.Redis;
using Nevma.ServiceDefaults.Extensions;
using Nevma.Messaging.Api.Application.Search;
using Nevma.Messaging.Api.Infrastructure.Search;

namespace Nevma.Messaging.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddMessagingService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<MessagingDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("MessagingDatabase"),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "messaging");
                    npgsqlOptions.EnableRetryOnFailure();
                }));
        services.AddMessagingAuthentication(configuration);
        var signalR = services.AddSignalR(options => options.MaximumReceiveMessageSize = 32 * 1024);
        services.AddScoped<IMessagingUnitOfWork>(provider =>
            provider.GetRequiredService<MessagingDbContext>());
        services.AddScoped<IConversationRepository, EfConversationRepository>();
        services.AddScoped<IMessageRepository, EfMessageRepository>();
        services.AddScoped<IFileAttachmentAuthorizer, HttpFileAttachmentAuthorizer>();
        services.AddScoped<IIntegrationEventInbox, EfIntegrationEventInbox>();
        services.AddScoped<IUserRealtimePublisher, SignalRUserRealtimePublisher>();
        var redisOptions = configuration
            .GetSection(RedisPresenceOptions.SectionName)
            .Get<RedisPresenceOptions>() ?? new RedisPresenceOptions();
        services.AddSingleton(redisOptions);
        if (redisOptions.Enabled)
        {
            if (string.IsNullOrWhiteSpace(redisOptions.ConnectionString))
                throw new InvalidOperationException("Redis:ConnectionString is required when Redis is enabled.");
            if (redisOptions.PresenceTtlSeconds < 30)
                throw new InvalidOperationException("Redis:PresenceTtlSeconds must be at least 30 seconds.");

            var redisConfiguration = ConfigurationOptions.Parse(redisOptions.ConnectionString);
            redisConfiguration.AbortOnConnectFail = false;
            redisConfiguration.ChannelPrefix = RedisChannel.Literal("nevma:messaging");
            services.AddSingleton<IConnectionMultiplexer>(
                _ => ConnectionMultiplexer.Connect(redisConfiguration));
            signalR.AddStackExchangeRedis(redisOptions.ConnectionString, options =>
            {
                options.Configuration.AbortOnConnectFail = false;
                options.Configuration.ChannelPrefix = RedisChannel.Literal("nevma:messaging");
            });
            services.AddSingleton<IUserPresenceTracker, RedisUserPresenceTracker>();
        }
        else
        {
            services.AddSingleton<IUserPresenceTracker, InMemoryUserPresenceTracker>();
        }
        services.AddScoped<ConversationService>();
        services.AddScoped<MessageService>();
        services.AddScoped<IMessagingSearchService, EfMessagingSearchService>();
        services.AddScoped<PlanningEventHandler>();
        services
            .AddHttpClient("Files", client =>
                client.BaseAddress = new Uri(configuration["Services:Files"] ?? "http://localhost:5106"))
            .AddNevmaResilience();
        services.AddHttpClient("Notifications", client =>
        {
            client.BaseAddress = new Uri(configuration["Services:Notifications"] ?? "http://localhost:5104");
            client.DefaultRequestHeaders.Add("X-Nevma-Internal-Key", configuration["InternalServices:Key"] ?? "nevma-local-internal");
        }).AddNevmaResilience();

        var brokerOptions = configuration
            .GetSection(MessageBrokerOptions.SectionName)
            .Get<MessageBrokerOptions>() ?? new MessageBrokerOptions();
        services.Configure<MessageBrokerOptions>(
            configuration.GetSection(MessageBrokerOptions.SectionName));
        if (brokerOptions.Enabled)
            services.AddHostedService<PlanningEventsConsumer>();
        return services;
    }
}

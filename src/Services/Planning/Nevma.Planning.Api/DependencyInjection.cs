using Microsoft.EntityFrameworkCore;
using Nevma.Planning.Api.Application;
using Nevma.Planning.Api.Infrastructure;
using Nevma.Planning.Api.Infrastructure.Authentication;
using Nevma.Planning.Api.Infrastructure.Persistence;
using Nevma.Planning.Api.Application.Tasks;
using Nevma.Planning.Api.Infrastructure.Tasks;
using Nevma.Planning.Api.Infrastructure.Outbox;
using Nevma.Planning.Api.Infrastructure.Messaging;
using Nevma.Planning.Api.Application.Search;
using Nevma.Planning.Api.Infrastructure.Search;

namespace Nevma.Planning.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddPlanningService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<PlanningDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("PlanningDatabase"),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "planning");
                    npgsqlOptions.EnableRetryOnFailure();
                }));
        services.AddPlanningAuthentication(configuration);
        services.AddScoped<IPlanningUnitOfWork>(provider => provider.GetRequiredService<PlanningDbContext>());
        services.AddScoped<IPlanningRepository, EfPlanningRepository>();
        services.AddScoped<IPlanningEventOutbox, EfPlanningEventOutbox>();
        services.AddScoped<OutboxStore>();
        services.AddScoped<PlanningService>();
        services.AddScoped<ITaskRepository, EfTaskRepository>();
        services.AddScoped<ITaskShareRepository, EfTaskShareRepository>();
        services.AddScoped<TaskService>();
        services.AddScoped<TaskSharingService>();
        services.AddScoped<IPlanningSearchService, EfPlanningSearchService>();
        services.AddScoped<TaskReminderProcessor>();
        services.AddHostedService<TaskReminderWorker>();
        services.AddScoped<CalendarReminderProcessor>();
        services.AddHostedService<CalendarReminderWorker>();

        var brokerOptions = configuration
            .GetSection(MessageBrokerOptions.SectionName)
            .Get<MessageBrokerOptions>() ?? new MessageBrokerOptions();
        services.Configure<MessageBrokerOptions>(
            configuration.GetSection(MessageBrokerOptions.SectionName));
        if (brokerOptions.Enabled)
        {
            services.AddSingleton<IIntegrationEventPublisher, RabbitMqIntegrationEventPublisher>();
            services.AddHostedService<PlanningOutboxDispatcher>();
        }
        return services;
    }
}

using Microsoft.EntityFrameworkCore;
using Nevma.Commands.Api.Application;
using Nevma.Commands.Api.Infrastructure.Authentication;
using Nevma.Commands.Api.Infrastructure.Ai;
using Nevma.Commands.Api.Infrastructure.Execution;
using Nevma.Commands.Api.Infrastructure.Parsing;
using Nevma.Commands.Api.Infrastructure.Persistence;
using Nevma.Commands.Api.Infrastructure.Speech;
using Nevma.ServiceDefaults.Extensions;

namespace Nevma.Commands.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddCommandsService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<CommandsDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("CommandsDatabase"),
            npgsql => { npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "commands"); npgsql.EnableRetryOnFailure(); }));
        services.AddCommandsAuthentication(configuration);
        services.AddScoped<ICommandsUnitOfWork>(provider => provider.GetRequiredService<CommandsDbContext>());
        services.AddScoped<ICommandRepository, EfCommandRepository>();
        services.Configure<AiProviderOptions>(configuration.GetSection(AiProviderOptions.SectionName));
        services.Configure<SpeechProviderOptions>(configuration.GetSection(SpeechProviderOptions.SectionName));
        services.AddSingleton<RuleBasedIntentParser>();
        services.AddSingleton<ICommandValidator, CommandValidator>();
        services
            .AddHttpClient<HttpAiProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddNevmaResilience(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(8));
        services
            .AddHttpClient<OpenAiSpeechToTextProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddNevmaResilience(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(45));
        services.AddTransient<IAiProvider, HttpAiProvider>();
        services
            .AddHttpClient<OpenAiAssistantProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddNevmaResilience(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(20));
        services.AddTransient<IAiAssistantProvider, OpenAiAssistantProvider>();
        services.AddTransient<ISpeechToTextProvider, OpenAiSpeechToTextProvider>();
        services.AddTransient<IIntentParser, HybridIntentParser>();
        services.AddScoped<ICommandExecutor, BackendCommandExecutor>();
        services.AddScoped<CommandService>();
        services.AddScoped<AssistantService>();
        services
            .AddHttpClient("Planning", client => client.BaseAddress = new Uri(configuration["Services:Planning"] ?? "http://localhost:5276"))
            .AddNevmaResilience();
        services
            .AddHttpClient("Messaging", client => client.BaseAddress = new Uri(configuration["Services:Messaging"] ?? "http://localhost:5085"))
            .AddNevmaResilience();
        return services;
    }
}

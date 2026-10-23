using System.Threading.RateLimiting;
using Nevma.Gateway.Home;
using Nevma.ServiceDefaults.Extensions;
using Nevma.Gateway.Search;

namespace Nevma.Gateway;

public static class DependencyInjection
{
    public static IServiceCollection AddGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"));
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy("Gateway", policy =>
        {
            if (origins.Length > 0)
                policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.StartsWithSegments("/health")
                    ? RateLimitPartition.GetNoLimiter("health")
                    : RateLimitPartition.GetFixedWindowLimiter(
                        GetRateLimitPartition(context),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 120,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));
        });
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<HomeService>();
        services.AddScoped<GlobalSearchService>();
        AddServiceClient(services, configuration, "Identity");
        AddServiceClient(services, configuration, "Planning");
        AddServiceClient(services, configuration, "Notifications");
        AddServiceClient(services, configuration, "Messaging");
        AddServiceClient(services, configuration, "Files");

        return services;
    }

    private static string GetRateLimitPartition(HttpContext context)
    {
        var developmentUserId = context.Request.Headers["X-Nevma-User-Id"].ToString();
        return Guid.TryParse(developmentUserId, out var userId)
            ? $"user:{userId:N}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }

    private static void AddServiceClient(
        IServiceCollection services,
        IConfiguration configuration,
        string name)
    {
        var address = configuration[$"Services:{name}"]
            ?? throw new InvalidOperationException($"Services:{name} is required.");
        services
            .AddHttpClient(name, client => client.BaseAddress = new Uri(address, UriKind.Absolute))
            .AddNevmaResilience(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(4));
    }
}

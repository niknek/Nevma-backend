using Nevma.ServiceDefaults.Middleware;

namespace Nevma.ServiceDefaults.Extensions;

public static class ApplicationBuilderExtensions
{
    public static WebApplication UseNevmaServiceDefaults(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (!app.Environment.IsDevelopment())
            app.UseHsts();

        if (!app.Environment.IsDevelopment() ||
            app.Configuration.GetValue("HttpsRedirection:Enabled", true))
        {
            app.UseHttpsRedirection();
        }

        return app;
    }
}

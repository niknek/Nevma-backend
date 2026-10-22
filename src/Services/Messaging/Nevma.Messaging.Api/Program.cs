using Nevma.Messaging.Api;
using Nevma.Messaging.Api.Endpoints;
using Nevma.Messaging.Api.Realtime;
using Nevma.ServiceDefaults.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddNevmaServiceDefaults(builder.Configuration);
builder.Services.AddMessagingService(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseNevmaServiceDefaults();
app.UseAuthentication();
if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.User.Identity?.IsAuthenticated != true &&
            Guid.TryParse(context.Request.Headers["X-Nevma-User-Id"], out var userId))
        {
            var identity = new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim("sub", userId.ToString())],
                "NevmaDevelopmentHeader");
            context.User = new System.Security.Claims.ClaimsPrincipal(identity);
        }
        await next();
    });
}
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapHub<ChatHub>("/hubs/chat").RequireAuthorization();
app.MapConversationEndpoints();
app.MapSearchEndpoints();

app.Run();

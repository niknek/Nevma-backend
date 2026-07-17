using Nevma.Gateway;
using Nevma.Gateway.Home;
using Nevma.Gateway.Search;
using Nevma.ServiceDefaults.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddNevmaServiceDefaults(builder.Configuration);
builder.Services.AddGateway(builder.Configuration);
builder.Services.AddGatewayAuthentication(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseNevmaServiceDefaults();
app.UseCors("Gateway");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    const long standardLimit = 1024 * 1024;
    const long uploadLimit = 26L * 1024 * 1024;
    var isUpload = context.Request.Path.StartsWithSegments("/files") ||
        context.Request.Path.StartsWithSegments("/commands/api/commands/transcribe");
    if (context.Request.ContentLength > (isUpload ? uploadLimit : standardLimit))
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }
    await next(context);
});
app.MapHealthChecks("/health").DisableRateLimiting();
app.MapHomeEndpoints();
app.MapSearchEndpoints();
app.MapReverseProxy();

app.Run();

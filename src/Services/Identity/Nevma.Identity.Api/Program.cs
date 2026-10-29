using Nevma.Identity.Api;
using Nevma.Identity.Api.Endpoints;
using Nevma.ServiceDefaults.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddNevmaServiceDefaults(builder.Configuration);
builder.Services.AddIdentityService(builder.Configuration, builder.Environment);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseNevmaServiceDefaults();
app.UseRateLimiter();
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
app.UseAntiforgery();
app.MapHealthChecks("/health");
app.MapAccountEndpoints();
app.MapExternalAuthenticationEndpoints();
app.MapAccountRecoveryEndpoints();
app.MapAuthenticationEndpoints();
app.MapMultiFactorAuthenticationEndpoints();
app.MapSecurityEventEndpoints();
app.MapContactConnectionEndpoints();
app.MapAddressBookEndpoints();
app.MapStoryAudienceGroupEndpoints();
app.MapDeviceSessionEndpoints();
app.MapOpenIddictEndpoints();
app.MapUserEndpoints();

app.Run();

public partial class Program;

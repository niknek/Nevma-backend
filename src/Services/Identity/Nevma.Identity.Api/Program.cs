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
app.MapDeviceSessionEndpoints();
app.MapOpenIddictEndpoints();
app.MapUserEndpoints();

app.Run();

public partial class Program;

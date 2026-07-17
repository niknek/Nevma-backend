using Nevma.Planning.Api;
using Nevma.Planning.Api.Endpoints;
using Nevma.ServiceDefaults.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddNevmaServiceDefaults(builder.Configuration);
builder.Services.AddPlanningService(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseNevmaServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapMeetingInvitationEndpoints();
app.MapCalendarEndpoints();
app.MapPersonalCalendarEndpoints();
app.MapTaskEndpoints();
app.MapSearchEndpoints();

app.Run();

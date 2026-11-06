using Microsoft.EntityFrameworkCore;
using Nevma.Identity.Api.Infrastructure.Persistence;

namespace Nevma.Identity.Api.Infrastructure.AddressBook;

public sealed class ContactJoinedNotice
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid JoinedUserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}

public sealed class ContactJoinedDelivery(IServiceScopeFactory scopes, IHttpClientFactory clients,
    ILogger<ContactJoinedDelivery> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var pending = await db.ContactJoinedNotices.Where(item => item.DeliveredAt == null)
                    .OrderBy(item => item.CreatedAt).Take(50).ToListAsync(stoppingToken);
                foreach (var notice in pending)
                {
                    using var response = await clients.CreateClient("ContactNotifications")
                        .PostAsJsonAsync("/api/notifications/internal/contact-joined", notice, stoppingToken);
                    response.EnsureSuccessStatusCode();
                    notice.DeliveredAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning(exception, "Contact notification delivery will retry."); }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}

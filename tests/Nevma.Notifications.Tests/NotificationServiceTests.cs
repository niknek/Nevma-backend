using System.Text;
using Microsoft.EntityFrameworkCore;
using Nevma.Contracts.Notifications;
using Nevma.Notifications.Api.Application.Notifications;
using Nevma.Notifications.Api.Application.PushDevices;
using Nevma.Notifications.Api.Infrastructure.Notifications;
using Nevma.Notifications.Api.Infrastructure.Persistence;
using Nevma.Notifications.Api.Infrastructure.PushDevices;

namespace Nevma.Notifications.Tests;

public sealed class NotificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Registered_push_token_is_not_stored_in_plain_text()
    {
        await using var context = CreateContext();
        var service = CreatePushDeviceService(context);
        var rawToken = "high-entropy-provider-token";

        var result = await service.RegisterAsync(
            Guid.NewGuid(),
            new RegisterPushDeviceRequest("phone-1", PushPlatform.Android, rawToken));

        Assert.True(result.IsSuccess);
        var stored = await context.PushDevices.SingleAsync();
        Assert.NotEqual(rawToken, stored.ProtectedToken);
        Assert.NotEqual(rawToken, stored.TokenHash);
        Assert.DoesNotContain(rawToken, stored.ProtectedToken, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Web_push_subscription_can_be_registered()
    {
        await using var context = CreateContext();
        var service = CreatePushDeviceService(context);
        var subscription = "{\"endpoint\":\"https://push.example.test/subscription\",\"keys\":{\"p256dh\":\"key\",\"auth\":\"secret\"}}";

        var result = await service.RegisterAsync(
            Guid.NewGuid(),
            new RegisterPushDeviceRequest("browser-1", PushPlatform.Web, subscription));

        Assert.True(result.IsSuccess);
        Assert.Equal(PushPlatform.Web, result.Device!.Platform);
        Assert.Equal((int)PushPlatform.Web, (int)(await context.PushDevices.SingleAsync()).Platform);
    }

    [Fact]
    public async Task Re_registering_a_device_refreshes_it_without_a_duplicate()
    {
        await using var context = CreateContext();
        var service = CreatePushDeviceService(context);
        var userId = Guid.NewGuid();
        var first = await service.RegisterAsync(
            userId,
            new RegisterPushDeviceRequest("phone-1", PushPlatform.Android, "old-token"));

        var refreshed = await service.RegisterAsync(
            userId,
            new RegisterPushDeviceRequest("phone-1", PushPlatform.Ios, "new-token"));

        Assert.Equal(first.Device!.Id, refreshed.Device!.Id);
        Assert.Equal(PushPlatform.Ios, refreshed.Device.Platform);
        Assert.Equal(1, await context.PushDevices.CountAsync());
        Assert.Equal(
            new FakeTokenProtector().Protect("new-token"),
            (await context.PushDevices.SingleAsync()).ProtectedToken);
    }

    [Fact]
    public async Task Another_user_cannot_revoke_a_push_device()
    {
        await using var context = CreateContext();
        var service = CreatePushDeviceService(context);
        var registered = await service.RegisterAsync(
            Guid.NewGuid(),
            new RegisterPushDeviceRequest("phone-1", PushPlatform.Android, "token"));

        var result = await service.RevokeAsync(registered.Device!.Id, Guid.NewGuid());

        Assert.IsType<RevokePushDeviceResult.NotFound>(result);
        Assert.True((await context.PushDevices.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Notifications_are_isolated_by_owner()
    {
        await using var context = CreateContext();
        var service = CreateNotificationService(context);
        var ownerId = Guid.NewGuid();
        var notification = await service.CreateAsync(
            ownerId,
            "meeting.changed",
            "Meeting update",
            "Open Nevma to review the change.");
        await service.CreateAsync(
            Guid.NewGuid(),
            "meeting.changed",
            "Someone else's update",
            "Private");

        var ownerList = await service.ListAsync(ownerId, 50);
        var forbidden = await service.MarkReadAsync(notification.Id, Guid.NewGuid());

        Assert.Collection(ownerList, item => Assert.Equal(notification.Id, item.Id));
        Assert.IsType<MarkNotificationReadResult.NotFound>(forbidden);
        Assert.Null((await context.Notifications.SingleAsync(item => item.Id == notification.Id)).ReadAt);
    }

    [Fact]
    public async Task Owner_can_mark_a_notification_as_read()
    {
        await using var context = CreateContext();
        var service = CreateNotificationService(context);
        var ownerId = Guid.NewGuid();
        var notification = await service.CreateAsync(
            ownerId,
            "meeting.changed",
            "Meeting update",
            "Open Nevma to review the change.");

        var result = await service.MarkReadAsync(notification.Id, ownerId);

        var read = Assert.IsType<MarkNotificationReadResult.Read>(result);
        Assert.Equal(Now, read.Notification.ReadAt);
    }

    private static PushDeviceService CreatePushDeviceService(NotificationsDbContext context) =>
        new(
            new EfPushDeviceRepository(context),
            new FakeTokenProtector(),
            context,
            new FixedTimeProvider(Now));

    private static NotificationService CreateNotificationService(NotificationsDbContext context) =>
        new(
            new EfNotificationRepository(context),
            context,
            new FixedTimeProvider(Now));

    private static NotificationsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase($"notifications-{Guid.NewGuid():N}")
            .Options;
        return new NotificationsDbContext(options);
    }

    private sealed class FakeTokenProtector : IPushTokenProtector
    {
        public string Protect(string token) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"scope:{token}"));
        public string Unprotect(string protectedToken) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(protectedToken))[6..];
        public string Hash(string token) => $"hash:{token}";
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

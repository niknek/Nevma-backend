using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nevma.Contracts.Integration;
using Nevma.Contracts.Planning;
using Nevma.Notifications.Api.Application.Integration;
using Nevma.Notifications.Api.Infrastructure.Inbox;
using Nevma.Notifications.Api.Infrastructure.Delivery;
using Nevma.Notifications.Api.Infrastructure.Notifications;
using Nevma.Notifications.Api.Infrastructure.Persistence;
using Nevma.Notifications.Api.Infrastructure.PushDevices;
using Nevma.Notifications.Api.Domain.PushDevices;

namespace Nevma.Notifications.Tests;

public sealed class PlanningEventHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pending_meeting_event_notifies_the_invitee_with_an_acceptance_prompt()
    {
        await using var context = CreateContext();
        var livePublisher = new RecordingLivePublisher();
        var handler = CreateHandler(context, livePublisher);
        var integrationEvent = CreateEvent();
        context.PushDevices.AddRange(
            PushDevice.Register(
                integrationEvent.OrganizerId,
                "organizer-phone",
                PushPlatform.Android,
                "protected-1",
                "hash-1",
                Now),
            PushDevice.Register(
                integrationEvent.InviteeId,
                "invitee-phone",
                PushPlatform.Ios,
                "protected-2",
                "hash-2",
                Now));
        await context.SaveChangesAsync();

        var result = await handler.HandleAsync(integrationEvent);

        Assert.Equal(PlanningEventHandleResult.Processed, result);
        var notifications = await context.Notifications.ToListAsync();
        Assert.Single(notifications);
        Assert.All(notifications, notification =>
        {
            Assert.Equal(integrationEvent.InviteeId, notification.UserId);
            Assert.Equal("New meeting invitation", notification.Title);
            Assert.Equal("Do you accept this meeting? Open Nevma to respond.", notification.Body);
            Assert.DoesNotContain(integrationEvent.Title, notification.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(integrationEvent.Location!, notification.Body, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(1, await context.InboxMessages.CountAsync());
        Assert.Equal(1, await context.DeliveryAttempts.CountAsync());
        Assert.Single(livePublisher.Notifications);
    }

    [Fact]
    public async Task Duplicate_meeting_event_does_not_duplicate_notifications()
    {
        await using var context = CreateContext();
        var handler = CreateHandler(context);
        var integrationEvent = CreateEvent();

        await handler.HandleAsync(integrationEvent);
        var duplicate = await handler.HandleAsync(integrationEvent);

        Assert.Equal(PlanningEventHandleResult.AlreadyProcessed, duplicate);
        Assert.Equal(1, await context.Notifications.CountAsync());
        Assert.Equal(1, await context.InboxMessages.CountAsync());
    }

    [Fact]
    public async Task Invalid_participants_do_not_create_notifications_or_inbox_state()
    {
        await using var context = CreateContext();
        var handler = CreateHandler(context);
        var integrationEvent = CreateEvent();
        integrationEvent = integrationEvent with { InviteeId = integrationEvent.OrganizerId };

        await Assert.ThrowsAsync<InvalidDataException>(() => handler.HandleAsync(integrationEvent));

        Assert.Empty(context.Notifications);
        Assert.Empty(context.InboxMessages);
    }

    private static PlanningEventHandler CreateHandler(
        NotificationsDbContext context,
        Nevma.Notifications.Api.Application.Notifications.ILiveNotificationPublisher? livePublisher = null) =>
        new(
            new EfNotificationRepository(context),
            new EfPushDeviceRepository(context),
            new EfDeliveryAttemptRepository(context),
            new EfIntegrationEventInbox(context),
            context,
            new FixedTimeProvider(Now),
            livePublisher ?? new NoOpLivePublisher(),
            NullLogger<PlanningEventHandler>.Instance);

    private static NotificationsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase($"notification-events-{Guid.NewGuid():N}")
            .Options;
        return new NotificationsDbContext(options);
    }

    private static MeetingInvitationChangedIntegrationEvent CreateEvent() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Private board meeting",
            Now.AddDays(1),
            TimeSpan.FromHours(1),
            "Private office",
            MeetingInvitationStatus.Pending,
            null,
            null,
            null,
            null,
            Now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoOpLivePublisher
        : Nevma.Notifications.Api.Application.Notifications.ILiveNotificationPublisher
    {
        public Task PublishAsync(
            Guid userId,
            Nevma.Contracts.Notifications.NotificationResponse notification,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingLivePublisher
        : Nevma.Notifications.Api.Application.Notifications.ILiveNotificationPublisher
    {
        public List<Nevma.Contracts.Notifications.NotificationResponse> Notifications { get; } = [];
        public Task PublishAsync(
            Guid userId,
            Nevma.Contracts.Notifications.NotificationResponse notification,
            CancellationToken cancellationToken = default)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}

using System.Net;
using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;
using Nevma.Contracts.Notifications;
using Nevma.Notifications.Api.Application.Delivery;
using WebPush;
using FirebaseNotification = FirebaseAdmin.Messaging.Notification;

namespace Nevma.Notifications.Api.Infrastructure.Delivery;

public sealed class MultiPlatformPushNotificationSender : IPushNotificationSender, IDisposable
{
    private readonly FirebaseApp? _firebaseApp;
    private readonly WebPushClient _webPushClient = new();
    private readonly WebPushOptions _webPush;

    public MultiPlatformPushNotificationSender(IOptions<PushDeliveryOptions> options)
    {
        _webPush = options.Value.WebPush;
        if (!string.IsNullOrWhiteSpace(options.Value.ProjectId))
        {
            _firebaseApp = FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.GetApplicationDefault(),
                ProjectId = options.Value.ProjectId
            }, "nevma-notifications");
        }
    }

    public Task<PushSendResult> SendAsync(
        PushPlatform platform,
        string target,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken = default) =>
        platform == PushPlatform.Web
            ? SendWebAsync(target, title, body, data, cancellationToken)
            : SendFirebaseAsync(target, title, body, data, cancellationToken);

    private async Task<PushSendResult> SendFirebaseAsync(
        string target,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        if (_firebaseApp is null)
            return new PushSendResult.RetryableFailure("FirebaseNotConfigured");
        var message = new Message
        {
#pragma warning disable CS0618
            Token = target,
#pragma warning restore CS0618
            Notification = new FirebaseNotification { Title = title, Body = body },
            Data = new Dictionary<string, string>(data)
        };
        try
        {
            var id = await FirebaseMessaging.GetMessaging(_firebaseApp)
                .SendAsync(message, false, cancellationToken);
            return new PushSendResult.Sent(id);
        }
        catch (FirebaseMessagingException exception) when (exception.MessagingErrorCode is
            MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument or MessagingErrorCode.SenderIdMismatch)
        {
            return new PushSendResult.PermanentFailure(exception.MessagingErrorCode?.ToString() ?? "PermanentFailure");
        }
        catch (FirebaseMessagingException exception)
        {
            return new PushSendResult.RetryableFailure(exception.MessagingErrorCode?.ToString() ?? exception.ErrorCode.ToString());
        }
    }

    private async Task<PushSendResult> SendWebAsync(
        string target,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        if (!_webPush.Enabled || string.IsNullOrWhiteSpace(_webPush.PublicKey) || string.IsNullOrWhiteSpace(_webPush.PrivateKey))
            return new PushSendResult.RetryableFailure("WebPushNotConfigured");
        WebSubscription? subscription;
        try { subscription = JsonSerializer.Deserialize<WebSubscription>(target, JsonOptions); }
        catch (JsonException) { return new PushSendResult.PermanentFailure("InvalidWebSubscription"); }
        if (subscription?.Endpoint is null || subscription.Keys?.P256dh is null || subscription.Keys.Auth is null)
            return new PushSendResult.PermanentFailure("InvalidWebSubscription");
        var payload = JsonSerializer.Serialize(new { title, body, data, url = "/" }, JsonOptions);
        try
        {
            await _webPushClient.SendNotificationAsync(
                new PushSubscription(subscription.Endpoint, subscription.Keys.P256dh, subscription.Keys.Auth),
                payload,
                new VapidDetails(_webPush.Subject, _webPush.PublicKey, _webPush.PrivateKey),
                cancellationToken);
            return new PushSendResult.Sent($"web-{Guid.NewGuid():N}");
        }
        catch (WebPushException exception) when (exception.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            return new PushSendResult.PermanentFailure($"WebPush{(int)exception.StatusCode}");
        }
        catch (WebPushException exception)
        {
            return new PushSendResult.RetryableFailure($"WebPush{(int)exception.StatusCode}");
        }
    }

    public void Dispose()
    {
        _firebaseApp?.Delete();
        _webPushClient.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record WebSubscription(string Endpoint, WebSubscriptionKeys Keys);
    private sealed record WebSubscriptionKeys(string P256dh, string Auth);
}

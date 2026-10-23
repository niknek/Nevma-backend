namespace Nevma.Notifications.Api.Infrastructure.Delivery;

public sealed class PushDeliveryOptions
{
    public const string SectionName = "PushDelivery";

    public bool Enabled { get; init; }
    public string? ProjectId { get; init; }
    public WebPushOptions WebPush { get; init; } = new();
}

public sealed class WebPushOptions
{
    public bool Enabled { get; init; }
    public string? PublicKey { get; init; }
    public string? PrivateKey { get; init; }
    public string Subject { get; init; } = "mailto:notifications@nevma.app";
}

namespace Nevma.Notifications.Api.Domain.PushDevices;

public sealed class PushDevice
{
    private PushDevice(
        Guid id,
        Guid userId,
        string deviceId,
        PushPlatform platform,
        string protectedToken,
        string tokenHash,
        DateTimeOffset registeredAt)
    {
        Id = id;
        UserId = userId;
        DeviceId = deviceId;
        Platform = platform;
        ProtectedToken = protectedToken;
        TokenHash = tokenHash;
        RegisteredAt = registeredAt;
        LastSeenAt = registeredAt;
    }

    public Guid Id { get; }
    public Guid UserId { get; }
    public string DeviceId { get; }
    public PushPlatform Platform { get; private set; }
    public string ProtectedToken { get; private set; }
    public string TokenHash { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset RegisteredAt { get; }
    public DateTimeOffset LastSeenAt { get; private set; }

    public static PushDevice Register(
        Guid userId,
        string deviceId,
        PushPlatform platform,
        string protectedToken,
        string tokenHash,
        DateTimeOffset registeredAt) =>
        new(
            Guid.NewGuid(),
            userId,
            deviceId.Trim(),
            platform,
            protectedToken,
            tokenHash,
            registeredAt);

    public void Refresh(
        PushPlatform platform,
        string protectedToken,
        string tokenHash,
        DateTimeOffset seenAt)
    {
        Platform = platform;
        ProtectedToken = protectedToken;
        TokenHash = tokenHash;
        IsActive = true;
        LastSeenAt = seenAt;
    }

    public bool Revoke()
    {
        if (!IsActive)
            return false;
        IsActive = false;
        return true;
    }
}

public enum PushPlatform
{
    Android,
    Ios,
    Web
}

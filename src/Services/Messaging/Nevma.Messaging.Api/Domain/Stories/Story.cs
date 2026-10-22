namespace Nevma.Messaging.Api.Domain.Stories;

public sealed class Story
{
    private Story() { }

    private Story(Guid id, Guid ownerId, Guid fileAssetId, string mediaType, string? caption,
        Guid[] audienceIds, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Id = id;
        OwnerId = ownerId;
        FileAssetId = fileAssetId;
        MediaType = mediaType;
        Caption = caption;
        AudienceIds = audienceIds;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid FileAssetId { get; private set; }
    public string MediaType { get; private set; } = string.Empty;
    public string? Caption { get; private set; }
    public Guid[] AudienceIds { get; private set; } = [];
    public Guid[] SeenByIds { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public static Story Create(Guid ownerId, Guid fileAssetId, string mediaType, string? caption,
        IReadOnlyCollection<Guid> audienceIds, DateTimeOffset now) =>
        new(Guid.NewGuid(), ownerId, fileAssetId, mediaType, string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
            audienceIds.Where(id => id != Guid.Empty && id != ownerId).Distinct().ToArray(), now, now.AddHours(24));

    public void MarkSeen(Guid userId)
    {
        if (userId != OwnerId && AudienceIds.Contains(userId) && !SeenByIds.Contains(userId))
            SeenByIds = [.. SeenByIds, userId];
    }
}

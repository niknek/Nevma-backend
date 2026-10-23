namespace Nevma.Notifications.Api.Domain.Notifications;

public sealed class Notification
{
    private Notification(
        Guid id,
        Guid userId,
        string type,
        string title,
        string body,
        DateTimeOffset createdAt,
        Guid? resourceId)
    {
        Id = id;
        UserId = userId;
        Type = type;
        Title = title;
        Body = body;
        CreatedAt = createdAt;
        ResourceId = resourceId;
    }

    public Guid Id { get; }
    public Guid UserId { get; }
    public string Type { get; }
    public string Title { get; }
    public string Body { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? ReadAt { get; private set; }
    public Guid? ResourceId { get; }

    public static Notification Create(
        Guid userId,
        string type,
        string title,
        string body,
        DateTimeOffset createdAt,
        Guid? resourceId = null) =>
        new(Guid.NewGuid(), userId, type, title.Trim(), body.Trim(), createdAt, resourceId);

    public bool MarkRead(DateTimeOffset readAt)
    {
        if (ReadAt is not null)
            return false;
        ReadAt = readAt;
        return true;
    }
}

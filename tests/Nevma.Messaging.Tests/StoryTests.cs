using Nevma.Messaging.Api.Domain.Stories;

namespace Nevma.Messaging.Tests;

public sealed class StoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 15, 0, 0, TimeSpan.Zero);
    [Fact]
    public void Likes_are_unique_reversible_and_limited_to_the_audience()
    {
        var owner = Guid.NewGuid(); var viewer = Guid.NewGuid();
        var story = Story.Create(owner, Guid.NewGuid(), "image/jpeg", null, [viewer], Now);
        story.SetLike(owner, true); story.SetLike(Guid.NewGuid(), true);
        Assert.Empty(story.LikedByIds);
        story.SetLike(viewer, true); story.SetLike(viewer, true);
        Assert.Equal([viewer], story.LikedByIds);
        story.SetLike(viewer, false); Assert.Empty(story.LikedByIds);
    }

    [Fact]
    public void Story_expires_after_twenty_four_hours_and_normalizes_audience()
    {
        var ownerId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();

        var story = Story.Create(ownerId, Guid.NewGuid(), "image/jpeg", "  Μια στιγμή  ",
            [viewerId, viewerId, ownerId, Guid.Empty], Now);

        Assert.Equal("Μια στιγμή", story.Caption);
        Assert.Equal([viewerId], story.AudienceIds);
        Assert.Equal(Now.AddHours(24), story.ExpiresAt);
    }

    [Fact]
    public void Only_audience_members_are_recorded_once_as_viewers()
    {
        var ownerId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        var story = Story.Create(ownerId, Guid.NewGuid(), "video/mp4", null, [viewerId], Now);

        story.MarkSeen(ownerId);
        story.MarkSeen(Guid.NewGuid());
        story.MarkSeen(viewerId);
        story.MarkSeen(viewerId);

        Assert.Equal([viewerId], story.SeenByIds);
    }
}

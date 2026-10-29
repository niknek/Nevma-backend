namespace Nevma.Identity.Api.Domain.Users;

public sealed class StoryAudienceGroup
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid[] MemberIds { get; set; } = [];
}

namespace Nevma.Identity.Api.Domain.Users;

public sealed class User
{
    private User(Guid id, string displayName, string? avatarUrl, DateTimeOffset createdAt)
    {
        Id = id;
        DisplayName = displayName;
        AvatarUrl = avatarUrl;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public string DisplayName { get; private set; }
    public string? AvatarUrl { get; private set; }
    public string? Bio { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? Phone { get; private set; }
    public bool ShowEmail { get; private set; }
    public bool ShowPhone { get; private set; }
    public string Availability { get; private set; } = "Available";
    public string StoryPrivacy { get; private set; } = "Contacts";
    public DateTimeOffset CreatedAt { get; }

    public static User Create(Guid id, string displayName, string? avatarUrl, DateTimeOffset createdAt) =>
        new(id, displayName.Trim(), avatarUrl?.Trim(), createdAt);

    public void Update(string displayName, string? avatarUrl)
    {
        DisplayName = displayName.Trim();
        AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl.Trim();
    }

    public void UpdateProfile(string displayName, string? avatarUrl, string? bio, string? contactEmail,
        string? phone, bool showEmail, bool showPhone, string availability, string storyPrivacy)
    {
        Update(displayName, avatarUrl);
        Bio = Normalize(bio);
        ContactEmail = Normalize(contactEmail);
        Phone = Normalize(phone);
        ShowEmail = showEmail;
        ShowPhone = showPhone;
        Availability = availability;
        StoryPrivacy = storyPrivacy;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

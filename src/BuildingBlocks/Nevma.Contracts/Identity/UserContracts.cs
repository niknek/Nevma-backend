namespace Nevma.Contracts.Identity;

public sealed record RegisterUserRequest(
    string Email,
    string Password,
    string DisplayName,
    string? AvatarUrl,
    string? Phone = null);

public sealed record UserSummary(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    string? Bio = null,
    string? ContactEmail = null,
    string? Phone = null,
    bool ShowEmail = false,
    bool ShowPhone = false,
    string Availability = "Available",
    string StoryPrivacy = "Contacts");

public sealed record UpdateProfileRequest(
    string DisplayName,
    string? AvatarUrl,
    string? Bio = null,
    string? ContactEmail = null,
    string? Phone = null,
    bool ShowEmail = false,
    bool ShowPhone = false,
    string Availability = "Available",
    string StoryPrivacy = "Contacts");

public sealed record UserSettingsResponse(
    string TimeZoneId,
    string Locale,
    bool AllowPresence,
    DateTimeOffset UpdatedAt);

public sealed record UpdateUserSettingsRequest(
    string TimeZoneId,
    string Locale,
    bool AllowPresence);

public sealed record ReportUserRequest(string Reason, string? Details);

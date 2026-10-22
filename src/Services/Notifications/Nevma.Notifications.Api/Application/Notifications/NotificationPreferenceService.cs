using Nevma.Contracts.Notifications;
using Nevma.Notifications.Api.Domain.Notifications;

namespace Nevma.Notifications.Api.Application.Notifications;

public sealed class NotificationPreferenceService(
    INotificationPreferenceRepository repository,
    INotificationsUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<NotificationPreferenceResponse> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var preference = await GetOrCreateAsync(userId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(preference);
    }

    public async Task<PreferenceUpdateResult> UpdateAsync(
        Guid userId,
        UpdateNotificationPreferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if ((request.QuietHoursStart is null) != (request.QuietHoursEnd is null))
            errors["quietHours"] = ["Quiet hours start and end must be provided together."];
        if (string.IsNullOrWhiteSpace(request.TimeZoneId) || request.TimeZoneId.Length > 100)
            errors[nameof(request.TimeZoneId)] = ["Time zone is required."];
        else
        {
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId); }
            catch (TimeZoneNotFoundException) { errors[nameof(request.TimeZoneId)] = ["Time zone is unknown."]; }
            catch (InvalidTimeZoneException) { errors[nameof(request.TimeZoneId)] = ["Time zone is invalid."]; }
        }
        if (errors.Count > 0)
            return new PreferenceUpdateResult.Invalid(errors);

        var preference = await GetOrCreateAsync(userId, cancellationToken);
        preference.Update(
            request.PushEnabled,
            request.MeetingNotificationsEnabled,
            request.CalendarRemindersEnabled,
            request.TaskRemindersEnabled,
            request.QuietHoursStart,
            request.QuietHoursEnd,
            request.TimeZoneId,
            timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new PreferenceUpdateResult.Updated(ToResponse(preference));
    }

    private async Task<NotificationPreference> GetOrCreateAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var preference = await repository.GetAsync(userId, cancellationToken);
        if (preference is not null)
            return preference;
        preference = NotificationPreference.CreateDefault(userId, timeProvider.GetUtcNow());
        await repository.AddAsync(preference, cancellationToken);
        return preference;
    }

    private static NotificationPreferenceResponse ToResponse(NotificationPreference preference) =>
        new(
            preference.PushEnabled,
            preference.MeetingNotificationsEnabled,
            preference.CalendarRemindersEnabled,
            preference.TaskRemindersEnabled,
            preference.QuietHoursStart,
            preference.QuietHoursEnd,
            preference.TimeZoneId,
            preference.UpdatedAt);
}

public abstract record PreferenceUpdateResult
{
    public sealed record Updated(NotificationPreferenceResponse Preference) : PreferenceUpdateResult;
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : PreferenceUpdateResult;
}

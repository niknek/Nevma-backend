using System.Collections.Concurrent;
using Nevma.Contracts.Messaging;
using Nevma.Messaging.Api.Application.Presence;

namespace Nevma.Messaging.Api.Infrastructure.Realtime;

public sealed class InMemoryUserPresenceTracker(TimeProvider timeProvider) : IUserPresenceTracker
{
    private readonly ConcurrentDictionary<Guid, PresenceState> _states = new();

    public Task<PresenceResponse> ConnectedAsync(Guid userId)
    {
        var state = _states.AddOrUpdate(
            userId,
            _ => new PresenceState(1, null, timeProvider.GetUtcNow()),
            (_, current) => current with { Connections = current.Connections + 1 });
        return Task.FromResult(ToResponse(userId, state));
    }

    public Task<PresenceResponse> DisconnectedAsync(Guid userId)
    {
        var now = timeProvider.GetUtcNow();
        var state = _states.AddOrUpdate(
            userId,
            _ => new PresenceState(0, now, null),
            (_, current) => new PresenceState(Math.Max(0, current.Connections - 1), now, null));
        return Task.FromResult(ToResponse(userId, state));
    }

    public Task<PresenceResponse> RefreshAsync(Guid userId)
    {
        var now = timeProvider.GetUtcNow();
        var state = _states.AddOrUpdate(
            userId,
            _ => new PresenceState(0, null, now),
            (_, current) => current with { RefreshedAt = now });
        return Task.FromResult(ToResponse(userId, state, now));
    }

    public Task<PresenceResponse> GetAsync(Guid userId) =>
        Task.FromResult(_states.TryGetValue(userId, out var state)
            ? ToResponse(userId, state, timeProvider.GetUtcNow())
            : new PresenceResponse(userId, false, null));

    private static PresenceResponse ToResponse(Guid userId, PresenceState state, DateTimeOffset? now = null)
    {
        var online = state.Connections > 0 ||
            (state.RefreshedAt is not null && now is not null && state.RefreshedAt > now.Value.AddSeconds(-15));
        return new PresenceResponse(userId, online, online ? null : state.LastSeenAt ?? state.RefreshedAt);
    }

    private sealed record PresenceState(int Connections, DateTimeOffset? LastSeenAt, DateTimeOffset? RefreshedAt);
}

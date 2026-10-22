using Nevma.Contracts.Messaging;
using Nevma.Messaging.Api.Application.Presence;
using StackExchange.Redis;

namespace Nevma.Messaging.Api.Infrastructure.Realtime;

public sealed class RedisUserPresenceTracker(
    IConnectionMultiplexer connection,
    RedisPresenceOptions options,
    TimeProvider timeProvider) : IUserPresenceTracker
{
    private const string ConnectedScript = """
        local count = redis.call('INCR', KEYS[1])
        redis.call('EXPIRE', KEYS[1], ARGV[1])
        redis.call('DEL', KEYS[2])
        return count
        """;
    private const string DisconnectedScript = """
        local count = tonumber(redis.call('GET', KEYS[1]) or '0')
        if count <= 1 then
            redis.call('DEL', KEYS[1])
            redis.call('SET', KEYS[2], ARGV[1], 'EX', ARGV[2])
            return 0
        end
        count = redis.call('DECR', KEYS[1])
        redis.call('EXPIRE', KEYS[1], ARGV[3])
        return count
        """;
    private const string RefreshScript = """
        local count = tonumber(redis.call('GET', KEYS[1]) or '0')
        if count <= 0 then
            redis.call('SET', KEYS[1], 1, 'EX', ARGV[1])
            count = 1
        else
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        return count
        """;

    private readonly IDatabase _database = connection.GetDatabase();

    public async Task<PresenceResponse> ConnectedAsync(Guid userId)
    {
        var keys = GetKeys(userId);
        var count = (long)await _database.ScriptEvaluateAsync(
            ConnectedScript,
            [keys.Connections, keys.LastSeen],
            [options.PresenceTtlSeconds]);
        return new PresenceResponse(userId, count > 0, null);
    }

    public async Task<PresenceResponse> DisconnectedAsync(Guid userId)
    {
        var now = timeProvider.GetUtcNow();
        var keys = GetKeys(userId);
        var count = (long)await _database.ScriptEvaluateAsync(
            DisconnectedScript,
            [keys.Connections, keys.LastSeen],
            [
                now.ToUnixTimeMilliseconds(),
                checked(options.LastSeenTtlDays * 24 * 60 * 60),
                options.PresenceTtlSeconds
            ]);
        return new PresenceResponse(userId, count > 0, count > 0 ? null : now);
    }

    public async Task<PresenceResponse> RefreshAsync(Guid userId)
    {
        var keys = GetKeys(userId);
        var count = (long)await _database.ScriptEvaluateAsync(
            RefreshScript,
            [keys.Connections],
            [options.PresenceTtlSeconds]);
        return count > 0
            ? new PresenceResponse(userId, true, null)
            : await GetAsync(userId);
    }

    public async Task<PresenceResponse> GetAsync(Guid userId)
    {
        var keys = GetKeys(userId);
        var values = await _database.StringGetAsync([keys.Connections, keys.LastSeen]);
        if (values[0].TryParse(out long connections) && connections > 0)
            return new PresenceResponse(userId, true, null);
        if (values[1].TryParse(out long lastSeenMilliseconds))
        {
            return new PresenceResponse(
                userId,
                false,
                DateTimeOffset.FromUnixTimeMilliseconds(lastSeenMilliseconds));
        }

        return new PresenceResponse(userId, false, null);
    }

    private static PresenceKeys GetKeys(Guid userId)
    {
        var prefix = $"nevma:presence:{userId:N}";
        return new PresenceKeys($"{prefix}:connections", $"{prefix}:last-seen");
    }

    private sealed record PresenceKeys(RedisKey Connections, RedisKey LastSeen);
}

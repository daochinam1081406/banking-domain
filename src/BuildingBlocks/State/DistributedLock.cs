using StackExchange.Redis;

namespace BuildingBlocks.State;

/// <summary>
/// Distributed lock trên Redis — chặn 2 instance cùng xử lý một thực thể (vd 2 giám định viên
/// bấm duyệt cùng lúc, hoặc cùng 1 người double-click). Lock có TTL nên process chết không kẹt khoá.
/// Nhả khoá bằng Lua so token để không xoá nhầm khoá của người khác khi mình đã hết hạn.
/// </summary>
public interface IDistributedLock
{
    Task<IAsyncDisposable?> AcquireAsync(string resource, TimeSpan ttl, CancellationToken ct = default);
}

public sealed class RedisDistributedLock(IConnectionMultiplexer redis) : IDistributedLock
{
    // Chỉ DEL nếu value đúng token của mình (atomic) — tránh xoá khoá người khác vừa chiếm.
    private const string ReleaseScript = """
        if redis.call("get", KEYS[1]) == ARGV[1] then
            return redis.call("del", KEYS[1])
        else
            return 0
        end
        """;

    public async Task<IAsyncDisposable?> AcquireAsync(
        string resource, TimeSpan ttl, CancellationToken ct = default)
    {
        var key = $"lock:{resource}";
        var token = Guid.NewGuid().ToString("N");
        var db = redis.GetDatabase();

        return await db.StringSetAsync(key, token, ttl, When.NotExists)
            ? new Handle(db, key, token)
            : null;   // không lấy được khoá — caller quyết định 409 hay chờ
    }

    private sealed class Handle(IDatabase db, string key, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
            => await db.ScriptEvaluateAsync(ReleaseScript, [key], [token]);
    }
}

public sealed class NoOpDistributedLock : IDistributedLock
{
    private sealed class Handle : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    public Task<IAsyncDisposable?> AcquireAsync(string resource, TimeSpan ttl, CancellationToken ct = default)
        => Task.FromResult<IAsyncDisposable?>(new Handle());
}

using StackExchange.Redis;

namespace BuildingBlocks.State;

/// <summary>
/// **Idempotency-Key** — pattern chuẩn của API tài chính (Stripe/Adyen dùng y hệt).
/// Client retry (mất mạng, bấm 2 lần, gateway timeout) gửi lại cùng key ⇒ server KHÔNG tạo giao dịch
/// thứ hai mà trả lại kết quả cũ. Redis giữ state này vì nó phải **dùng chung giữa nhiều instance**
/// (in-memory sẽ hỏng ngay khi scale ra 2 pod).
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Chiếm quyền xử lý key. False = đã có request khác xử lý (đang chạy hoặc xong).</summary>
    Task<bool> TryBeginAsync(string key, TimeSpan ttl, CancellationToken ct = default);
    Task<string?> GetResultAsync(string key, CancellationToken ct = default);
    Task SaveResultAsync(string key, string result, TimeSpan ttl, CancellationToken ct = default);
    Task ReleaseAsync(string key, CancellationToken ct = default);
}

public sealed class RedisIdempotencyStore(IConnectionMultiplexer redis) : IIdempotencyStore
{
    private static string Lock(string key) => $"idem:lock:{key}";
    private static string Result(string key) => $"idem:res:{key}";

    // SET NX = atomic "chỉ set nếu chưa tồn tại" → chống race giữa nhiều instance.
    public async Task<bool> TryBeginAsync(string key, TimeSpan ttl, CancellationToken ct = default)
        => await redis.GetDatabase().StringSetAsync(Lock(key), "1", ttl, When.NotExists);

    public async Task<string?> GetResultAsync(string key, CancellationToken ct = default)
        => await redis.GetDatabase().StringGetAsync(Result(key));

    public Task SaveResultAsync(string key, string result, TimeSpan ttl, CancellationToken ct = default)
        => redis.GetDatabase().StringSetAsync(Result(key), result, ttl);

    /// <summary>Nhả khoá khi xử lý lỗi — để client retry được ngay, không phải chờ hết TTL.</summary>
    public Task ReleaseAsync(string key, CancellationToken ct = default)
        => redis.GetDatabase().KeyDeleteAsync(Lock(key));
}

public sealed class NoOpIdempotencyStore : IIdempotencyStore
{
    public Task<bool> TryBeginAsync(string key, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult(true);
    public Task<string?> GetResultAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task SaveResultAsync(string key, string result, TimeSpan ttl, CancellationToken ct = default) => Task.CompletedTask;
    public Task ReleaseAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
}

using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BuildingBlocks.Auth;

public sealed class LoginThrottleOptions
{
    /// <summary>Số lần sai liên tiếp cho 1 tài khoản trước khi khoá tạm.</summary>
    public int MaxAttemptsPerAccount { get; set; } = 5;
    /// <summary>Số lần sai từ cùng 1 IP (chống dò nhiều tài khoản).</summary>
    public int MaxAttemptsPerIp { get; set; } = 20;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
}

public sealed class TooManyLoginAttemptsException(string message, TimeSpan retryAfter) : Exception(message)
{
    public string ErrorCode => "TOO_MANY_ATTEMPTS";
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>
/// Chống brute-force đăng nhập. Bộ đếm nằm trên Redis vì in-memory thì attacker chỉ cần xoay
/// vòng instance là vượt giới hạn. Đếm theo cả tài khoản (dò một người) lẫn IP (password
/// spraying — dò một mật khẩu trên nhiều tài khoản, lớp theo-tài-khoản không thấy được).
/// Đếm cả user không tồn tại, nếu không chênh lệch thời gian phản hồi sẽ lộ tài khoản nào có thật.
/// </summary>
public interface ILoginThrottle
{
    Task EnsureNotLockedAsync(string username, string? ipAddress, CancellationToken ct = default);
    Task RecordFailureAsync(string username, string? ipAddress, CancellationToken ct = default);
    Task ResetAsync(string username, CancellationToken ct = default);
}

public sealed class RedisLoginThrottle(
    IConnectionMultiplexer redis,
    LoginThrottleOptions options,
    ILogger<RedisLoginThrottle> logger) : ILoginThrottle
{
    private static string AccountKey(string u) => $"login:fail:acct:{u.Trim().ToLowerInvariant()}";
    private static string IpKey(string ip) => $"login:fail:ip:{ip}";

    public async Task EnsureNotLockedAsync(string username, string? ipAddress, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();

        var accountFails = (int?)await db.StringGetAsync(AccountKey(username)) ?? 0;
        if (accountFails >= options.MaxAttemptsPerAccount)
        {
            var ttl = await db.KeyTimeToLiveAsync(AccountKey(username)) ?? options.LockoutDuration;
            logger.LogWarning("Tài khoản {Username} bị khoá tạm do {Count} lần sai", username, accountFails);
            throw new TooManyLoginAttemptsException(
                "Quá nhiều lần đăng nhập sai. Vui lòng thử lại sau.", ttl);
        }

        if (string.IsNullOrWhiteSpace(ipAddress)) return;

        var ipFails = (int?)await db.StringGetAsync(IpKey(ipAddress)) ?? 0;
        if (ipFails >= options.MaxAttemptsPerIp)
        {
            var ttl = await db.KeyTimeToLiveAsync(IpKey(ipAddress)) ?? options.LockoutDuration;
            logger.LogWarning("IP {Ip} bị chặn do {Count} lần đăng nhập sai", ipAddress, ipFails);
            throw new TooManyLoginAttemptsException(
                "Quá nhiều lần đăng nhập sai từ địa chỉ này. Vui lòng thử lại sau.", ttl);
        }
    }

    public async Task RecordFailureAsync(string username, string? ipAddress, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await IncrementAsync(db, AccountKey(username));
        if (!string.IsNullOrWhiteSpace(ipAddress)) await IncrementAsync(db, IpKey(ipAddress));
    }

    /// <summary>Giữ bộ đếm IP để một lần đăng nhập đúng không tự mở khoá cho attacker.</summary>
    public Task ResetAsync(string username, CancellationToken ct = default)
        => redis.GetDatabase().KeyDeleteAsync(AccountKey(username));

    private async Task IncrementAsync(IDatabase db, string key)
    {
        var count = await db.StringIncrementAsync(key);
        // TTL chỉ đặt ở lần sai đầu tiên, nếu không attacker thử liên tục là gia hạn được cửa sổ.
        if (count == 1) await db.KeyExpireAsync(key, options.Window);
    }
}

/// <summary>Dùng khi chưa cấu hình Redis (dev/local) — không chặn gì.</summary>
public sealed class NoOpLoginThrottle : ILoginThrottle
{
    public Task EnsureNotLockedAsync(string username, string? ip, CancellationToken ct = default) => Task.CompletedTask;
    public Task RecordFailureAsync(string username, string? ip, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResetAsync(string username, CancellationToken ct = default) => Task.CompletedTask;
}

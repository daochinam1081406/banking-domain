using System.Text.Json;
using Accounts.Application;
using StackExchange.Redis;

namespace Accounts.Infrastructure;

internal static class CacheKeys
{
    public static string Account(string number) => $"account:{number.Trim().ToUpperInvariant()}";
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
}

/// <summary>Cache-aside decorator cho read số dư — hit Redis trước, miss thì EF rồi set cache.</summary>
public sealed class CachedAccountReadService(EfAccountReadService inner, IConnectionMultiplexer redis)
    : IAccountReadService
{
    public async Task<AccountDto?> GetByNumberAsync(string number, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var key = CacheKeys.Account(number);

        var cached = await db.StringGetAsync(key);
        if (cached.HasValue) return JsonSerializer.Deserialize<AccountDto>(cached!);

        var dto = await inner.GetByNumberAsync(number, ct);
        if (dto is not null)
            await db.StringSetAsync(key, JsonSerializer.Serialize(dto), CacheKeys.Ttl);
        return dto;
    }

    // Danh sách đổi khi mở tài khoản mới → đọc thẳng, không cache.
    public Task<IReadOnlyList<AccountDto>> ListAsync(string ownerId, CancellationToken ct = default)
        => inner.ListAsync(ownerId, ct);
}

public sealed class RedisAccountCacheInvalidator(IConnectionMultiplexer redis) : IAccountCacheInvalidator
{
    public Task InvalidateAsync(string number, CancellationToken ct = default)
        => redis.GetDatabase().KeyDeleteAsync(CacheKeys.Account(number));
}

public sealed class NoOpAccountCacheInvalidator : IAccountCacheInvalidator
{
    public Task InvalidateAsync(string number, CancellationToken ct = default) => Task.CompletedTask;
}

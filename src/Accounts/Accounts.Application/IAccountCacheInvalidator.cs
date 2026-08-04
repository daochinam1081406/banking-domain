namespace Accounts.Application;

/// <summary>Invalidate cache số dư sau khi thay đổi (impl: Redis hoặc No-op nếu tắt cache).</summary>
public interface IAccountCacheInvalidator
{
    Task InvalidateAsync(string number, CancellationToken ct = default);
}

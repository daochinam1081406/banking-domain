using System.Collections.Concurrent;

namespace Accounts.Api;

/// <summary>
/// Phase 1 walking skeleton: số dư in-memory để chứng minh luồng event chạy end-to-end.
/// Phase 2: thay bằng PostgreSQL (EF Core) + Account aggregate (DDD) + Outbox.
/// </summary>
public sealed class AccountStore
{
    private readonly ConcurrentDictionary<string, decimal> _balances = new();

    public decimal GetBalance(string accountId) => _balances.GetValueOrDefault(accountId, 0m);

    public void Apply(string fromAccount, string toAccount, decimal amount)
    {
        _balances.AddOrUpdate(fromAccount, -amount, (_, b) => b - amount);
        _balances.AddOrUpdate(toAccount, amount, (_, b) => b + amount);
    }
}

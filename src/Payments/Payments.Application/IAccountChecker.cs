namespace Payments.Application;

public sealed record AccountCheckResult(bool Exists, decimal Balance, string Currency, string OwnerId);

/// <summary>Sync check tài khoản nguồn (impl: gRPC gọi Accounts service).</summary>
public interface IAccountChecker
{
    Task<AccountCheckResult> CheckAsync(string number, CancellationToken ct = default);
}

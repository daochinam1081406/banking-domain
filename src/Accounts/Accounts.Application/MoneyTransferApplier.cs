using Accounts.Domain;
using Accounts.Domain.Exceptions;

namespace Accounts.Application;

/// <summary>
/// Use case: áp 1 lệnh chuyển tiền lên read model số dư (debit from, credit to).
/// Consumer gọi handler này khi nhận MoneyTransferred từ Service Bus.
/// Idempotency + ordering do consumer/broker đảm bảo; use case chỉ lo invariant domain.
/// </summary>
public sealed class MoneyTransferApplier(IAccountRepository repository)
{
    public async Task ApplyAsync(
        string fromAccount, string toAccount, decimal amount, string currency, CancellationToken ct = default)
    {
        var from = await repository.GetByNumberAsync(fromAccount, ct)
            ?? throw new AccountsDomainException($"Account {fromAccount} không tồn tại.", "ACCOUNT_NOT_FOUND");
        var to = await repository.GetByNumberAsync(toAccount, ct)
            ?? throw new AccountsDomainException($"Account {toAccount} không tồn tại.", "ACCOUNT_NOT_FOUND");

        from.Debit(amount);
        to.Credit(amount);
        await repository.SaveChangesAsync(ct);
    }
}

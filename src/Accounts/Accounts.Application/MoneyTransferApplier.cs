using Accounts.Domain;
using Accounts.Domain.Exceptions;

namespace Accounts.Application;

/// <summary>Kết quả áp lệnh chuyển tiền — consumer dùng để phát event saga về Payments.</summary>
public sealed record ApplyTransferOutcome(bool Applied, bool Duplicate, string? ErrorCode, string? Reason)
{
    public static ApplyTransferOutcome Ok() => new(true, false, null, null);
    public static ApplyTransferOutcome AlreadyApplied() => new(false, true, null, null);
    public static ApplyTransferOutcome Failed(string code, string reason) => new(false, false, code, reason);
}

/// <summary>
/// Use case: áp 1 lệnh chuyển tiền lên số dư — **idempotent** qua inbox (messageId).
/// Cập nhật số dư + đánh dấu message đã xử lý trong CÙNG 1 SaveChanges (atomic).
/// Lỗi nghiệp vụ (số dư không đủ / account không tồn tại) là **permanent** → trả Failed để
/// consumer phát compensating event, KHÔNG retry vô ích.
/// </summary>
public sealed class MoneyTransferApplier(
    IAccountRepository repository,
    IInboxStore inbox,
    IAccountCacheInvalidator cache)
{
    public async Task<ApplyTransferOutcome> ApplyAsync(
        string messageId,
        string fromAccount,
        string toAccount,
        decimal amount,
        CancellationToken ct = default)
    {
        if (await inbox.AlreadyProcessedAsync(messageId, ct))
            return ApplyTransferOutcome.AlreadyApplied();

        var from = await repository.GetByNumberAsync(fromAccount, ct);
        if (from is null)
            return ApplyTransferOutcome.Failed("ACCOUNT_NOT_FOUND", $"Account {fromAccount} không tồn tại.");

        var to = await repository.GetByNumberAsync(toAccount, ct);
        if (to is null)
            return ApplyTransferOutcome.Failed("ACCOUNT_NOT_FOUND", $"Account {toAccount} không tồn tại.");

        try
        {
            from.Debit(amount);
            to.Credit(amount);
        }
        catch (AccountsDomainException ex)
        {
            return ApplyTransferOutcome.Failed(ex.ErrorCode, ex.Message);
        }

        inbox.MarkProcessed(messageId);
        await repository.SaveChangesAsync(ct);   // số dư + inbox commit cùng nhau

        await cache.InvalidateAsync(fromAccount, ct);
        await cache.InvalidateAsync(toAccount, ct);
        return ApplyTransferOutcome.Ok();
    }
}

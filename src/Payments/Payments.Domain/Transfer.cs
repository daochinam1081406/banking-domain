using Payments.Domain.Exceptions;

namespace Payments.Domain;

/// <summary>Aggregate — 1 lệnh chuyển tiền được ghi nhận rồi phát event qua Outbox.</summary>
public sealed class Transfer
{
    public Guid Id { get; private set; }
    public string FromAccount { get; private set; } = null!;
    public string ToAccount { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "VND";
    public string Status { get; private set; } = "Initiated";
    public DateTimeOffset CreatedAt { get; private set; }

    private Transfer() { }

    public static Transfer Initiate(string fromAccount, string toAccount, decimal amount, string currency = "VND")
    {
        if (string.IsNullOrWhiteSpace(fromAccount) || string.IsNullOrWhiteSpace(toAccount))
            throw new PaymentsDomainException("Tài khoản nguồn/đích bắt buộc.", "ACCOUNT_REQUIRED");
        var from = fromAccount.Trim().ToUpperInvariant();
        var to = toAccount.Trim().ToUpperInvariant();
        if (from == to)
            throw new PaymentsDomainException("Không thể chuyển cho chính tài khoản đó.", "SAME_ACCOUNT");
        if (amount <= 0)
            throw new PaymentsDomainException("Amount phải > 0.", "AMOUNT_INVALID");

        return new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccount = from,
            ToAccount = to,
            Amount = amount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant(),
            Status = "Initiated",
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}

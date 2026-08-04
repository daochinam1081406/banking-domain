using Accounts.Domain.Exceptions;

namespace Accounts.Domain;

/// <summary>
/// Aggregate root — bảo vệ invariant số dư (không cho overdraft).
/// EF Core map trực tiếp aggregate này (private setter + private ctor).
/// </summary>
public sealed class Account
{
    public Guid Id { get; private set; }
    public string Number { get; private set; } = null!;
    /// <summary>Chủ sở hữu (JWT subject) — không có thì ai cũng rút được tiền tài khoản người khác.</summary>
    public string OwnerId { get; private set; } = null!;
    public decimal Balance { get; private set; }
    public string Currency { get; private set; } = "VND";
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency — chặn lost update khi nhiều message cùng account xử lý song song.</summary>
    public byte[] RowVersion { get; private set; } = [];

    private Account() { } // EF Core

    public static Account Open(string number, string ownerId, decimal initialBalance = 0, string currency = "VND")
    {
        if (string.IsNullOrWhiteSpace(number))
            throw new AccountsDomainException("Account number bắt buộc.", "NUMBER_REQUIRED");
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new AccountsDomainException("Chủ sở hữu bắt buộc.", "OWNER_REQUIRED");
        if (initialBalance < 0)
            throw new AccountsDomainException("Số dư ban đầu không được âm.", "BALANCE_INVALID");

        return new Account
        {
            Id = Guid.NewGuid(),
            Number = number.Trim().ToUpperInvariant(),
            OwnerId = ownerId.Trim(),
            Balance = initialBalance,
            Currency = currency.Trim().ToUpperInvariant(),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public bool IsOwnedBy(string ownerId) => OwnerId.Equals(ownerId?.Trim(), StringComparison.Ordinal);

    /// <summary>Chặn cộng/trừ khác loại tiền — nếu không sẽ "tạo tiền" (50.000 VND vào ví USD).</summary>
    public void EnsureCurrency(string currency)
    {
        if (!Currency.Equals(currency?.Trim().ToUpperInvariant(), StringComparison.Ordinal))
            throw new AccountsDomainException(
                $"Tài khoản {Number} dùng {Currency}, không khớp {currency}.", "CURRENCY_MISMATCH");
    }

    public void Credit(decimal amount)
    {
        EnsurePositive(amount);
        Balance += amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Debit(decimal amount)
    {
        EnsurePositive(amount);
        if (Balance < amount)
            throw new AccountsDomainException(
                $"Số dư không đủ: {Balance} < {amount}.", "INSUFFICIENT_FUNDS");
        Balance -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void EnsurePositive(decimal amount)
    {
        if (amount <= 0)
            throw new AccountsDomainException("Amount phải > 0.", "AMOUNT_INVALID");
    }
}

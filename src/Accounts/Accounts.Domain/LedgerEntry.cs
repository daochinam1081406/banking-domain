namespace Accounts.Domain;

public enum LedgerDirection
{
    Debit = 1,   // tiền ra khỏi tài khoản
    Credit = 2,  // tiền vào tài khoản
}

/// <summary>
/// Bút toán kép (double-entry) — bản ghi **bất biến** của mọi biến động tiền.
/// Mỗi lệnh chuyển tiền sinh đúng 2 entry (Debit bên gửi + Credit bên nhận) với cùng TransferId,
/// nên tổng Debit luôn = tổng Credit → đối soát được, không "tạo tiền" từ không khí.
/// Balance trên Account là giá trị chốt nhanh; ledger mới là nguồn sự thật để audit/sao kê.
/// </summary>
public sealed class LedgerEntry
{
    public Guid Id { get; private set; }
    public string AccountNumber { get; private set; } = null!;
    public Guid TransferId { get; private set; }
    public LedgerDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public decimal BalanceAfter { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private LedgerEntry() { } // EF Core

    public static LedgerEntry For(
        string accountNumber, Guid transferId, LedgerDirection direction,
        decimal amount, string currency, decimal balanceAfter) => new()
    {
        Id = Guid.NewGuid(),
        AccountNumber = accountNumber,
        TransferId = transferId,
        Direction = direction,
        Amount = amount,
        Currency = currency,
        BalanceAfter = balanceAfter,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}

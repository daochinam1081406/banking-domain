using Accounts.Domain;

namespace Accounts.Infrastructure;

/// <summary>Seed vài tài khoản demo để thử luồng chuyển tiền (idempotent — chỉ tạo khi chưa có).</summary>
public static class AccountSeeder
{
    public static void Seed(AccountsDbContext db)
    {
        if (db.Accounts.Any()) return;
        db.Accounts.Add(Account.Open("ACC-001", "demo", 10_000_000m));
        db.Accounts.Add(Account.Open("ACC-002", "demo", 5_000_000m));
        db.Accounts.Add(Account.Open("ACC-003", "demo", 0m));
        // Quỹ chi trả bồi thường của công ty bảo hiểm (nguồn tiền cho saga claim payout)
        db.Accounts.Add(Account.Open("INS-FUND", "insurer", 1_000_000_000m));
        db.SaveChanges();
    }
}

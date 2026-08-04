using Accounts.Application;
using Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Infrastructure;

public sealed class EfLedgerRepository(AccountsDbContext db) : ILedgerRepository
{
    // Chỉ add vào change tracker — commit chung SaveChanges với số dư (atomic).
    public void Add(LedgerEntry entry) => db.LedgerEntries.Add(entry);

    public async Task<IReadOnlyList<LedgerEntryDto>> GetStatementAsync(
        string accountNumber, int limit = 50, CancellationToken ct = default)
    {
        var number = accountNumber.Trim().ToUpperInvariant();
        return await db.LedgerEntries.AsNoTracking()
            .Where(l => l.AccountNumber == number)
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .Select(l => new LedgerEntryDto(
                l.Id, l.AccountNumber, l.TransferId, l.Direction.ToString(),
                l.Amount, l.Currency, l.BalanceAfter, l.CreatedAt))
            .ToListAsync(ct);
    }
}

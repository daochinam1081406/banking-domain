using Accounts.Domain;

namespace Accounts.Application;

/// <summary>Ghi/đọc bút toán. Entry được add cùng change tracker và commit chung SaveChanges với số dư.</summary>
public interface ILedgerRepository
{
    void Add(LedgerEntry entry);
    Task<IReadOnlyList<LedgerEntryDto>> GetStatementAsync(
        string accountNumber, int limit = 50, CancellationToken ct = default);
}

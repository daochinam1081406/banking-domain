using Accounts.Application;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Infrastructure;

/// <summary>Read side — AsNoTracking, trả DTO trực tiếp.</summary>
public sealed class EfAccountReadService(AccountsDbContext db) : IAccountReadService
{
    public async Task<AccountDto?> GetByNumberAsync(string number, CancellationToken ct = default)
    {
        var a = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Number == number.Trim().ToUpperInvariant(), ct);
        return a is null ? null : new AccountDto(a.Number, a.Balance, a.Currency, a.UpdatedAt);
    }

    public async Task<IReadOnlyList<AccountDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.Accounts.AsNoTracking()
            .OrderBy(a => a.Number)
            .Select(a => new AccountDto(a.Number, a.Balance, a.Currency, a.UpdatedAt))
            .ToListAsync(ct);
        return rows;
    }
}

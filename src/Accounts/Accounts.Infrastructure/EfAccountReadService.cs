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
}

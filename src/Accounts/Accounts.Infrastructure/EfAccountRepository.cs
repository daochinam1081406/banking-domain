using Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Infrastructure;

/// <summary>Write side — EF Core tracking để aggregate ghi số dư.</summary>
public sealed class EfAccountRepository(AccountsDbContext db) : IAccountRepository
{
    public Task<Account?> GetByNumberAsync(string number, CancellationToken ct = default)
        => db.Accounts.FirstOrDefaultAsync(a => a.Number == number.Trim().ToUpperInvariant(), ct);

    public async Task AddAsync(Account account, CancellationToken ct = default)
        => await db.Accounts.AddAsync(account, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

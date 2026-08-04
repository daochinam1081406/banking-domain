namespace Accounts.Domain;

public interface IAccountRepository
{
    Task<Account?> GetByNumberAsync(string number, CancellationToken ct = default);
    Task AddAsync(Account account, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

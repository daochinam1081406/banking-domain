namespace Accounts.Application;

/// <summary>Query side — đọc read model số dư.</summary>
public interface IAccountReadService
{
    Task<AccountDto?> GetByNumberAsync(string number, CancellationToken ct = default);
    Task<IReadOnlyList<AccountDto>> ListAsync(string ownerId, CancellationToken ct = default);
}

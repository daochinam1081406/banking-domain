namespace Insurance.Domain;

public interface IPolicyRepository
{
    Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Policy?> GetByNumberAsync(string policyNumber, CancellationToken ct = default);
    Task AddAsync(Policy policy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IClaimRepository
{
    Task<Claim?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Claim claim, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

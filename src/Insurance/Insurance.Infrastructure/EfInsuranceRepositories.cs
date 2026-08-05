using Insurance.Application;
using Insurance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Insurance.Infrastructure;

public sealed class EfPolicyRepository(InsuranceDbContext db) : IPolicyRepository
{
    public Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Policies.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Policy?> GetByNumberAsync(string policyNumber, CancellationToken ct = default)
        => db.Policies.FirstOrDefaultAsync(p => p.PolicyNumber == policyNumber.Trim().ToUpperInvariant(), ct);

    public async Task AddAsync(Policy policy, CancellationToken ct = default) => await db.Policies.AddAsync(policy, ct);
    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public sealed class EfClaimRepository(InsuranceDbContext db) : IClaimRepository
{
    public Task<Claim?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Claims.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task AddAsync(Claim claim, CancellationToken ct = default) => await db.Claims.AddAsync(claim, ct);
    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public sealed class EfInsuranceReadService(InsuranceDbContext db) : IInsuranceReadService
{
    public async Task<IReadOnlyList<PolicyDto>> ListPoliciesAsync(string holderId, CancellationToken ct = default)
        => await db.Policies.AsNoTracking()
            .Where(p => p.PolicyHolderId == holderId)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => Map(p)).ToListAsync(ct);

    public async Task<PolicyDto?> GetPolicyAsync(string policyNumber, CancellationToken ct = default)
    {
        var number = policyNumber.Trim().ToUpperInvariant();
        return await db.Policies.AsNoTracking()
            .Where(p => p.PolicyNumber == number).Select(p => Map(p)).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ClaimDto>> ListClaimsAsync(string claimantId, CancellationToken ct = default)
        => await db.Claims.AsNoTracking()
            .Where(c => c.ClaimantId == claimantId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => MapClaim(c)).ToListAsync(ct);

    public async Task<IReadOnlyList<ClaimDto>> ListAllClaimsAsync(CancellationToken ct = default)
        => await db.Claims.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => MapClaim(c)).ToListAsync(ct);

    public async Task<ClaimDto?> GetClaimAsync(Guid claimId, CancellationToken ct = default)
        => await db.Claims.AsNoTracking().Where(c => c.Id == claimId).Select(c => MapClaim(c)).FirstOrDefaultAsync(ct);

    private static PolicyDto Map(Policy p) => new(
        p.Id, p.PolicyNumber, p.PolicyHolderId, p.ProductCode,
        p.CoverageAmount, p.ClaimedAmount, p.CoverageAmount - p.ClaimedAmount,
        p.PremiumAmount, p.Currency, p.PayoutAccount,
        p.Status.ToString(), p.EffectiveFrom, p.EffectiveTo);

    private static ClaimDto MapClaim(Claim c) => new(
        c.Id, c.ClaimNumber, c.PolicyNumber, c.ClaimantId,
        c.RequestedAmount, c.AssessedCost, c.ApprovedAmount, c.Currency,
        c.IncidentDate, c.Description, c.Status.ToString(),
        c.ReviewerId, c.DecisionReason, c.PayoutTransferId, c.CreatedAt);
}

/// <summary>Số hợp đồng/hồ sơ theo năm — dùng sequence của Postgres để an toàn multi-instance.</summary>
public sealed class PostgresInsuranceNumberGenerator(InsuranceDbContext db) : IInsuranceNumberGenerator
{
    public async Task<string> NextPolicyNumberAsync(CancellationToken ct = default)
        => $"POL-{DateTime.UtcNow:yyyy}-{await NextAsync("policy_seq", ct):D6}";

    public async Task<string> NextClaimNumberAsync(CancellationToken ct = default)
        => $"CLM-{DateTime.UtcNow:yyyy}-{await NextAsync("claim_seq", ct):D6}";

    private async Task<long> NextAsync(string sequence, CancellationToken ct)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open)
            await db.Database.OpenConnectionAsync(ct);
        cmd.CommandText = $"SELECT nextval('{sequence}')";
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }
}

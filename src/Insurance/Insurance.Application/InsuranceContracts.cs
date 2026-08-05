namespace Insurance.Application;

public sealed record PolicyDto(
    Guid Id, string PolicyNumber, string PolicyHolderId, string ProductCode,
    decimal CoverageAmount, decimal ClaimedAmount, decimal RemainingCoverage,
    decimal PremiumAmount, string Currency, string PayoutAccount,
    string Status, DateOnly EffectiveFrom, DateOnly EffectiveTo);

public sealed record ClaimDto(
    Guid Id, string ClaimNumber, string PolicyNumber, string ClaimantId,
    decimal RequestedAmount, decimal? ApprovedAmount, string Currency,
    DateOnly IncidentDate, string Description, string Status,
    string? ReviewerId, string? DecisionReason, Guid? PayoutTransferId, DateTimeOffset CreatedAt);

public interface IInsuranceReadService
{
    Task<IReadOnlyList<PolicyDto>> ListPoliciesAsync(string holderId, CancellationToken ct = default);
    Task<PolicyDto?> GetPolicyAsync(string policyNumber, CancellationToken ct = default);
    Task<IReadOnlyList<ClaimDto>> ListClaimsAsync(string claimantId, CancellationToken ct = default);
    Task<ClaimDto?> GetClaimAsync(Guid claimId, CancellationToken ct = default);
}

/// <summary>Sinh số hợp đồng / số hồ sơ bồi thường.</summary>
public interface IInsuranceNumberGenerator
{
    Task<string> NextPolicyNumberAsync(CancellationToken ct = default);
    Task<string> NextClaimNumberAsync(CancellationToken ct = default);
}

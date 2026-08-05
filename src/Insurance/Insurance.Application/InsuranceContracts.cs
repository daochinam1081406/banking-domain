namespace Insurance.Application;

public sealed record PolicyDto(
    Guid Id, string PolicyNumber, string PolicyHolderId, string ProductCode,
    decimal CoverageAmount, decimal ClaimedAmount, decimal RemainingCoverage,
    decimal PremiumAmount, decimal Deductible, decimal CoPaymentRate, int WaitingPeriodDays,
    string Currency, string PayoutAccount,
    string Status, DateOnly EffectiveFrom, DateOnly EffectiveTo);

public sealed record ClaimDto(
    Guid Id, string ClaimNumber, string PolicyNumber, string ClaimantId,
    decimal RequestedAmount, decimal? AssessedCost, decimal? ApprovedAmount, string Currency,
    DateOnly IncidentDate, string Description, string Status,
    string? ReviewerId, string? DecisionReason, Guid? PayoutTransferId, DateTimeOffset CreatedAt);

/// <summary>Trần số dòng cho endpoint danh sách — không có trần thì một client kéo được cả bảng.</summary>
public static class QueryLimits
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public static int Clamp(int? requested) =>
        requested is null or < 1 ? DefaultPageSize : Math.Min(requested.Value, MaxPageSize);
}

public interface IInsuranceReadService
{
    Task<IReadOnlyList<PolicyDto>> ListPoliciesAsync(
        string holderId, int limit = QueryLimits.DefaultPageSize, CancellationToken ct = default);
    Task<PolicyDto?> GetPolicyAsync(string policyNumber, CancellationToken ct = default);
    Task<IReadOnlyList<ClaimDto>> ListClaimsAsync(
        string claimantId, int limit = QueryLimits.DefaultPageSize, CancellationToken ct = default);

    /// <summary>Hàng chờ giám định — chỉ hồ sơ đang chờ xử lý, có phân trang.</summary>
    Task<IReadOnlyList<ClaimDto>> ListPendingClaimsAsync(
        int limit = QueryLimits.DefaultPageSize, CancellationToken ct = default);
    Task<ClaimDto?> GetClaimAsync(Guid claimId, CancellationToken ct = default);
}

/// <summary>Sinh số hợp đồng / số hồ sơ bồi thường.</summary>
public interface IInsuranceNumberGenerator
{
    Task<string> NextPolicyNumberAsync(CancellationToken ct = default);
    Task<string> NextClaimNumberAsync(CancellationToken ct = default);
}

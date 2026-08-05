using BuildingBlocks.Contracts;
using Insurance.Domain;
using Insurance.Domain.Exceptions;

namespace Insurance.Application;

// ── Commands ─────────────────────────────────────────────────────
public sealed record IssuePolicyCommand(
    string ProductCode, decimal CoverageAmount, decimal PremiumAmount,
    string PayoutAccount, DateOnly EffectiveFrom, DateOnly EffectiveTo, string? Currency)
{
    public string PolicyHolderId { get; init; } = string.Empty;   // gán từ JWT
}

public sealed record SubmitClaimCommand(
    string PolicyNumber, decimal RequestedAmount, DateOnly IncidentDate, string Description)
{
    public string ClaimantId { get; init; } = string.Empty;       // gán từ JWT
}

public sealed record ApproveClaimCommand(Guid ClaimId, decimal ApprovedAmount)
{
    public string ReviewerId { get; init; } = string.Empty;
}

public sealed record RejectClaimCommand(Guid ClaimId, string Reason)
{
    public string ReviewerId { get; init; } = string.Empty;
}

/// <summary>
/// Nghiệp vụ bảo hiểm: phát hành hợp đồng · đóng phí · yêu cầu bồi thường · giám định duyệt/từ chối.
/// Duyệt bồi thường sẽ phát `ClaimApproved` để Payments chi trả (saga xuyên service).
/// </summary>
public sealed class InsuranceService(
    IPolicyRepository policies,
    IClaimRepository claims,
    IInsuranceNumberGenerator numbers,
    IClaimPayoutPublisher payouts)
{
    public async Task<PolicyIssuedResult> IssuePolicyAsync(IssuePolicyCommand cmd, CancellationToken ct = default)
    {
        var number = await numbers.NextPolicyNumberAsync(ct);
        var policy = Policy.Issue(
            number, cmd.PolicyHolderId, cmd.ProductCode, cmd.CoverageAmount, cmd.PremiumAmount,
            cmd.PayoutAccount, cmd.EffectiveFrom, cmd.EffectiveTo, cmd.Currency ?? "VND");

        await policies.AddAsync(policy, ct);
        await policies.SaveChangesAsync(ct);
        return new PolicyIssuedResult(policy.Id, policy.PolicyNumber, policy.Status.ToString());
    }

    /// <summary>Đóng phí → hợp đồng có hiệu lực (thực tế sẽ đối soát thanh toán trước).</summary>
    public async Task ActivatePolicyAsync(string policyNumber, string holderId, CancellationToken ct = default)
    {
        var policy = await LoadOwnedPolicyAsync(policyNumber, holderId, ct);
        policy.Activate();
        await policies.SaveChangesAsync(ct);
    }

    public async Task<ClaimSubmittedResult> SubmitClaimAsync(SubmitClaimCommand cmd, CancellationToken ct = default)
    {
        var policy = await LoadOwnedPolicyAsync(cmd.PolicyNumber, cmd.ClaimantId, ct);
        var number = await numbers.NextClaimNumberAsync(ct);

        var claim = Claim.Submit(number, policy, cmd.ClaimantId, cmd.RequestedAmount, cmd.IncidentDate, cmd.Description);
        await claims.AddAsync(claim, ct);
        await claims.SaveChangesAsync(ct);

        return new ClaimSubmittedResult(claim.Id, claim.ClaimNumber, claim.Status.ToString());
    }

    public async Task ApproveClaimAsync(ApproveClaimCommand cmd, CancellationToken ct = default)
    {
        var claim = await claims.GetByIdAsync(cmd.ClaimId, ct)
            ?? throw new InsuranceDomainException("Hồ sơ bồi thường không tồn tại.", "CLAIM_NOT_FOUND");
        var policy = await policies.GetByIdAsync(claim.PolicyId, ct)
            ?? throw new InsuranceDomainException("Hợp đồng không tồn tại.", "POLICY_NOT_FOUND");

        claim.Approve(cmd.ReviewerId, cmd.ApprovedAmount, policy);
        await claims.SaveChangesAsync(ct);   // claim + policy cùng DbContext → 1 transaction

        // Saga: báo Payments chi trả cho khách.
        await payouts.PublishApprovedAsync(new ClaimApprovedIntegrationEvent
        {
            ClaimId       = claim.Id,
            ClaimNumber   = claim.ClaimNumber,
            PolicyNumber  = claim.PolicyNumber,
            PayoutAccount = policy.PayoutAccount,
            Amount        = cmd.ApprovedAmount,
            Currency      = claim.Currency,
        }, ct);
    }

    public async Task RejectClaimAsync(RejectClaimCommand cmd, CancellationToken ct = default)
    {
        var claim = await claims.GetByIdAsync(cmd.ClaimId, ct)
            ?? throw new InsuranceDomainException("Hồ sơ bồi thường không tồn tại.", "CLAIM_NOT_FOUND");
        claim.Reject(cmd.ReviewerId, cmd.Reason);
        await claims.SaveChangesAsync(ct);
    }

    private async Task<Policy> LoadOwnedPolicyAsync(string policyNumber, string holderId, CancellationToken ct)
    {
        var policy = await policies.GetByNumberAsync(policyNumber, ct)
            ?? throw new InsuranceDomainException($"Hợp đồng {policyNumber} không tồn tại.", "POLICY_NOT_FOUND");
        if (!policy.IsOwnedBy(holderId))
            throw new InsuranceDomainException("Không có quyền với hợp đồng này.", "FORBIDDEN_POLICY");
        return policy;
    }
}

public sealed record PolicyIssuedResult(Guid PolicyId, string PolicyNumber, string Status);
public sealed record ClaimSubmittedResult(Guid ClaimId, string ClaimNumber, string Status);

/// <summary>Cổng phát event chi trả (impl ở Infrastructure — outbox + IEventBus).</summary>
public interface IClaimPayoutPublisher
{
    Task PublishApprovedAsync(ClaimApprovedIntegrationEvent @event, CancellationToken ct = default);
}

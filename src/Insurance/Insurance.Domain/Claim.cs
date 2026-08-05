using Insurance.Domain.Exceptions;

namespace Insurance.Domain;

public enum ClaimStatus
{
    Submitted = 1,     // KH gửi yêu cầu
    UnderReview = 2,   // giám định viên đang xử lý
    Approved = 3,      // đã duyệt, chờ chi trả
    Rejected = 4,
    Paid = 5,          // đã chi trả xong (Payments báo về)
}

/// <summary>
/// Yêu cầu bồi thường. Vòng đời: Submitted → UnderReview → Approved → **Paid** | Rejected.
/// `Paid` không do người dùng bấm — do saga: Approved ⇒ Payments chi trả ⇒ báo về mới chuyển Paid.
/// </summary>
public sealed class Claim
{
    public Guid Id { get; private set; }
    public string ClaimNumber { get; private set; } = null!;
    public Guid PolicyId { get; private set; }
    public string PolicyNumber { get; private set; } = null!;
    public string ClaimantId { get; private set; } = null!;
    public decimal RequestedAmount { get; private set; }
    public decimal? AssessedCost { get; private set; }      // chi phí GĐV công nhận
    public decimal? ApprovedAmount { get; private set; }    // số BH thực chi trả
    public string Currency { get; private set; } = "VND";
    public DateOnly IncidentDate { get; private set; }
    public string Description { get; private set; } = null!;
    public ClaimStatus Status { get; private set; }
    public string? ReviewerId { get; private set; }
    public string? DecisionReason { get; private set; }
    public Guid? PayoutTransferId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Claim() { } // EF Core

    public static Claim Submit(
        string claimNumber, Policy policy, string claimantId,
        decimal requestedAmount, DateOnly incidentDate, string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new InsuranceDomainException("Mô tả sự cố bắt buộc.", "DESCRIPTION_REQUIRED");
        if (!policy.IsOwnedBy(claimantId))
            throw new InsuranceDomainException(
                "Chỉ chủ hợp đồng mới được yêu cầu bồi thường.", "FORBIDDEN_POLICY");

        policy.EnsureClaimable(requestedAmount, incidentDate);

        return new Claim
        {
            Id = Guid.NewGuid(),
            ClaimNumber = claimNumber.Trim().ToUpperInvariant(),
            PolicyId = policy.Id,
            PolicyNumber = policy.PolicyNumber,
            ClaimantId = claimantId.Trim(),
            RequestedAmount = requestedAmount,
            Currency = policy.Currency,
            IncidentDate = incidentDate,
            Description = description.Trim(),
            Status = ClaimStatus.Submitted,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void StartReview(string reviewerId)
    {
        EnsureStatus(ClaimStatus.Submitted);
        ReviewerId = reviewerId.Trim();
        Status = ClaimStatus.UnderReview;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Giám định duyệt: `assessedCost` là chi phí được công nhận (≤ số yêu cầu, GĐV hay cắt giảm).
    /// Số **thực chi trả** do hợp đồng quyết định qua miễn thường + đồng chi trả + hạn mức còn lại.
    /// </summary>
    public void Approve(string reviewerId, decimal assessedCost, Policy policy)
    {
        EnsureStatus(ClaimStatus.Submitted, ClaimStatus.UnderReview);
        if (assessedCost <= 0)
            throw new InsuranceDomainException("Chi phí được duyệt phải > 0.", "AMOUNT_INVALID");
        if (assessedCost > RequestedAmount)
            throw new InsuranceDomainException(
                "Không thể duyệt cao hơn số yêu cầu.", "APPROVED_EXCEEDS_REQUESTED");

        var payable = policy.CalculatePayable(assessedCost);
        if (payable <= 0)
            throw new InsuranceDomainException(
                "Sau miễn thường/đồng chi trả, số tiền chi trả bằng 0.", "NOTHING_PAYABLE");

        policy.ConsumeCoverage(payable);   // trừ hạn mức theo số THỰC TRẢ

        AssessedCost = assessedCost;
        ApprovedAmount = payable;
        ReviewerId = reviewerId.Trim();
        Status = ClaimStatus.Approved;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Reject(string reviewerId, string reason)
    {
        EnsureStatus(ClaimStatus.Submitted, ClaimStatus.UnderReview);
        if (string.IsNullOrWhiteSpace(reason))
            throw new InsuranceDomainException("Lý do từ chối bắt buộc.", "REASON_REQUIRED");
        ReviewerId = reviewerId.Trim();
        DecisionReason = reason.Trim();
        Status = ClaimStatus.Rejected;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Payments báo đã chi trả xong (saga) — idempotent để message lặp không gây lỗi.</summary>
    public void MarkPaid(Guid payoutTransferId)
    {
        if (Status == ClaimStatus.Paid) return;
        EnsureStatus(ClaimStatus.Approved);
        PayoutTransferId = payoutTransferId;
        Status = ClaimStatus.Paid;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Chi trả thất bại — quay lại Approved để xử lý lại (compensating).</summary>
    public void MarkPayoutFailed(string reason)
    {
        if (Status != ClaimStatus.Approved) return;
        DecisionReason = $"Chi trả thất bại: {reason}";
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureStatus(params ClaimStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            throw new InsuranceDomainException(
                $"Yêu cầu bồi thường đang {Status}, không hợp lệ cho thao tác này.", "INVALID_STATUS");
    }
}

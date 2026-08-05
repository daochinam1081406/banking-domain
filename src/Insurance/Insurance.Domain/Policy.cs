using Insurance.Domain.Exceptions;

namespace Insurance.Domain;

public enum PolicyStatus
{
    Draft = 1,      // vừa tạo, chưa đóng phí
    Active = 2,     // đã đóng phí, có hiệu lực
    Lapsed = 3,     // hết hạn / mất hiệu lực
    Cancelled = 4,
}

/// <summary>
/// Hợp đồng bảo hiểm. Chỉ hợp đồng **Active** và còn trong thời hạn mới được bồi thường.
/// `CoverageAmount` là hạn mức tổng — tổng bồi thường đã duyệt không được vượt (tracked qua ClaimedAmount).
/// </summary>
public sealed class Policy
{
    public Guid Id { get; private set; }
    public string PolicyNumber { get; private set; } = null!;
    public string PolicyHolderId { get; private set; } = null!;   // JWT subject của chủ hợp đồng
    public string ProductCode { get; private set; } = null!;      // HEALTH / MOTOR / LIFE …
    public decimal CoverageAmount { get; private set; }           // số tiền bảo hiểm (hạn mức)
    public decimal PremiumAmount { get; private set; }            // phí bảo hiểm
    public string Currency { get; private set; } = "VND";
    public string PayoutAccount { get; private set; } = null!;    // tài khoản nhận tiền bồi thường
    public PolicyStatus Status { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly EffectiveTo { get; private set; }
    public decimal ClaimedAmount { get; private set; }            // tổng đã duyệt bồi thường
    public DateTimeOffset UpdatedAt { get; private set; }

    public decimal RemainingCoverage => CoverageAmount - ClaimedAmount;

    private Policy() { } // EF Core

    public static Policy Issue(
        string policyNumber, string policyHolderId, string productCode,
        decimal coverageAmount, decimal premiumAmount, string payoutAccount,
        DateOnly effectiveFrom, DateOnly effectiveTo, string currency = "VND")
    {
        if (string.IsNullOrWhiteSpace(policyNumber))
            throw new InsuranceDomainException("Số hợp đồng bắt buộc.", "POLICY_NUMBER_REQUIRED");
        if (string.IsNullOrWhiteSpace(policyHolderId))
            throw new InsuranceDomainException("Chủ hợp đồng bắt buộc.", "HOLDER_REQUIRED");
        if (string.IsNullOrWhiteSpace(payoutAccount))
            throw new InsuranceDomainException("Tài khoản nhận bồi thường bắt buộc.", "PAYOUT_ACCOUNT_REQUIRED");
        if (coverageAmount <= 0)
            throw new InsuranceDomainException("Số tiền bảo hiểm phải > 0.", "COVERAGE_INVALID");
        if (premiumAmount < 0)
            throw new InsuranceDomainException("Phí bảo hiểm không được âm.", "PREMIUM_INVALID");
        if (effectiveTo <= effectiveFrom)
            throw new InsuranceDomainException("Ngày hết hiệu lực phải sau ngày hiệu lực.", "PERIOD_INVALID");

        return new Policy
        {
            Id = Guid.NewGuid(),
            PolicyNumber = policyNumber.Trim().ToUpperInvariant(),
            PolicyHolderId = policyHolderId.Trim(),
            ProductCode = productCode.Trim().ToUpperInvariant(),
            CoverageAmount = coverageAmount,
            PremiumAmount = premiumAmount,
            Currency = currency.Trim().ToUpperInvariant(),
            PayoutAccount = payoutAccount.Trim().ToUpperInvariant(),
            Status = PolicyStatus.Draft,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            ClaimedAmount = 0,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Đóng phí → hợp đồng có hiệu lực.</summary>
    public void Activate()
    {
        if (Status != PolicyStatus.Draft)
            throw new InsuranceDomainException($"Hợp đồng đang {Status}, không thể kích hoạt.", "INVALID_STATUS");
        Status = PolicyStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        if (Status is PolicyStatus.Cancelled or PolicyStatus.Lapsed)
            throw new InsuranceDomainException($"Hợp đồng đã {Status}.", "INVALID_STATUS");
        Status = PolicyStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsOwnedBy(string holderId) => PolicyHolderId.Equals(holderId?.Trim(), StringComparison.Ordinal);

    /// <summary>Điều kiện bồi thường: hợp đồng Active, sự cố trong thời hạn, còn hạn mức.</summary>
    public void EnsureClaimable(decimal amount, DateOnly incidentDate)
    {
        if (Status != PolicyStatus.Active)
            throw new InsuranceDomainException(
                $"Hợp đồng đang {Status}, chỉ hợp đồng Active mới được bồi thường.", "POLICY_NOT_ACTIVE");
        if (incidentDate < EffectiveFrom || incidentDate > EffectiveTo)
            throw new InsuranceDomainException(
                $"Sự cố {incidentDate:yyyy-MM-dd} ngoài thời hạn bảo hiểm ({EffectiveFrom:yyyy-MM-dd}–{EffectiveTo:yyyy-MM-dd}).",
                "OUTSIDE_COVERAGE_PERIOD");
        if (amount <= 0)
            throw new InsuranceDomainException("Số tiền yêu cầu phải > 0.", "AMOUNT_INVALID");
        if (amount > RemainingCoverage)
            throw new InsuranceDomainException(
                $"Vượt hạn mức còn lại: {amount} > {RemainingCoverage}.", "EXCEEDS_COVERAGE");
    }

    /// <summary>Ghi nhận số tiền đã duyệt vào hạn mức đã dùng.</summary>
    public void ConsumeCoverage(decimal amount)
    {
        if (amount > RemainingCoverage)
            throw new InsuranceDomainException("Vượt hạn mức còn lại.", "EXCEEDS_COVERAGE");
        ClaimedAmount += amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

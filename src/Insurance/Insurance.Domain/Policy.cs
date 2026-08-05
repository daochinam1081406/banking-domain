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
/// Hợp đồng bảo hiểm. Chỉ hợp đồng Active và còn trong thời hạn mới được bồi thường.
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
    public decimal Deductible { get; private set; }               // mức miễn thường — KH tự chịu phần này
    public decimal CoPaymentRate { get; private set; }            // tỷ lệ đồng chi trả (0.2 = KH chịu 20%)
    public int WaitingPeriodDays { get; private set; }            // thời gian chờ kể từ ngày hiệu lực
    public string Currency { get; private set; } = "VND";
    public string PayoutAccount { get; private set; } = null!;    // tài khoản nhận tiền bồi thường
    public PolicyStatus Status { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly EffectiveTo { get; private set; }
    public decimal ClaimedAmount { get; private set; }            // tổng đã duyệt bồi thường
    public Guid? PremiumTransferId { get; private set; }          // giao dịch thu phí (bancassurance)
    public DateTimeOffset UpdatedAt { get; private set; }

    public decimal RemainingCoverage => CoverageAmount - ClaimedAmount;

    private Policy() { } // EF Core

    public static Policy Issue(
        string policyNumber, string policyHolderId, string productCode,
        decimal coverageAmount, decimal premiumAmount, string payoutAccount,
        DateOnly effectiveFrom, DateOnly effectiveTo, string currency = "VND",
        decimal deductible = 0, decimal coPaymentRate = 0, int waitingPeriodDays = 0)
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
        if (deductible < 0 || deductible >= coverageAmount)
            throw new InsuranceDomainException("Mức miễn thường không hợp lệ.", "DEDUCTIBLE_INVALID");
        if (coPaymentRate is < 0 or >= 1)
            throw new InsuranceDomainException("Tỷ lệ đồng chi trả phải trong [0,1).", "COPAY_INVALID");

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
            Deductible = deductible,
            CoPaymentRate = coPaymentRate,
            WaitingPeriodDays = waitingPeriodDays,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Chỉ chuyển Active khi ĐÃ THU ĐƯỢC PHÍ (Payments báo về qua saga) — không cho phép
    /// kích hoạt hợp đồng mà chưa có tiền, đây là nguyên tắc cơ bản của bảo hiểm.
    /// </summary>
    public void MarkPremiumCollected(Guid transferId)
    {
        if (Status == PolicyStatus.Active) return;   // idempotent
        if (Status != PolicyStatus.Draft)
            throw new InsuranceDomainException($"Hợp đồng đang {Status}, không thể kích hoạt.", "INVALID_STATUS");
        PremiumTransferId = transferId;
        Status = PolicyStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void EnsurePendingPremium()
    {
        if (Status != PolicyStatus.Draft)
            throw new InsuranceDomainException($"Hợp đồng đang {Status}, không cần đóng phí.", "INVALID_STATUS");
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

        // Thời gian chờ: sự cố xảy ra quá sớm sau khi mua thì không được bồi thường
        // (BH sức khoẻ VN: 30 ngày bệnh thông thường, 365 ngày thai sản) — chống trục lợi.
        var waitingEnds = EffectiveFrom.AddDays(WaitingPeriodDays);
        if (WaitingPeriodDays > 0 && incidentDate < waitingEnds)
            throw new InsuranceDomainException(
                $"Còn trong thời gian chờ {WaitingPeriodDays} ngày (đến {waitingEnds:yyyy-MM-dd}).",
                "WITHIN_WAITING_PERIOD");
        if (amount <= 0)
            throw new InsuranceDomainException("Số tiền yêu cầu phải > 0.", "AMOUNT_INVALID");
        if (amount > RemainingCoverage)
            throw new InsuranceDomainException(
                $"Vượt hạn mức còn lại: {amount} > {RemainingCoverage}.", "EXCEEDS_COVERAGE");
    }

    /// <summary>
    /// Số tiền công ty BH thực trả, theo đúng công thức ngành:
    ///   (chi phí − mức miễn thường) × (1 − tỷ lệ đồng chi trả), chặn trên bởi hạn mức còn lại.
    /// Ví dụ: viện phí 10tr, miễn thường 1tr, đồng chi trả 20% → BH trả (10−1)×0.8 = 7,2tr.
    /// </summary>
    public decimal CalculatePayable(decimal claimedCost)
    {
        var afterDeductible = Math.Max(0, claimedCost - Deductible);
        var payable = afterDeductible * (1 - CoPaymentRate);
        return Math.Min(payable, RemainingCoverage);
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

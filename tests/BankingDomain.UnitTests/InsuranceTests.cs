using Insurance.Domain;
using Insurance.Domain.Exceptions;
using Xunit;

namespace BankingDomain.UnitTests;

public class InsuranceTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    private static Policy ActivePolicy(decimal coverage = 100_000_000m)
    {
        var p = Policy.Issue("POL-2026-000001", "alice", "HEALTH", coverage, 2_000_000m,
            "ACC-001", From, To);
        p.MarkPremiumCollected(Guid.NewGuid());   // thu phí xong mới Active
        return p;
    }

    // ── Hợp đồng ─────────────────────────────────────────────────
    [Fact]
    public void Issue_ShouldStartDraft_NotClaimable()
    {
        var p = Policy.Issue("POL-1", "alice", "MOTOR", 50_000_000m, 1_000_000m, "ACC-001", From, To);

        Assert.Equal(PolicyStatus.Draft, p.Status);
        var ex = Assert.Throws<InsuranceDomainException>(
            () => p.EnsureClaimable(1_000_000m, new DateOnly(2026, 6, 1)));
        Assert.Equal("POLICY_NOT_ACTIVE", ex.ErrorCode);   // chưa đóng phí thì chưa được bồi thường
    }

    [Fact]
    public void Issue_InvalidPeriod_ShouldThrow()
    {
        var ex = Assert.Throws<InsuranceDomainException>(() =>
            Policy.Issue("POL-1", "alice", "HEALTH", 1_000m, 10m, "ACC-001", To, From));
        Assert.Equal("PERIOD_INVALID", ex.ErrorCode);
    }

    [Fact]
    public void EnsureClaimable_IncidentOutsidePeriod_ShouldThrow()
    {
        var p = ActivePolicy();
        var ex = Assert.Throws<InsuranceDomainException>(
            () => p.EnsureClaimable(1_000_000m, new DateOnly(2025, 12, 31)));
        Assert.Equal("OUTSIDE_COVERAGE_PERIOD", ex.ErrorCode);
    }

    [Fact]
    public void EnsureClaimable_ExceedCoverage_ShouldThrow()
    {
        var p = ActivePolicy(coverage: 10_000_000m);
        var ex = Assert.Throws<InsuranceDomainException>(
            () => p.EnsureClaimable(20_000_000m, new DateOnly(2026, 6, 1)));
        Assert.Equal("EXCEEDS_COVERAGE", ex.ErrorCode);
    }

    // ── Bồi thường ───────────────────────────────────────────────
    [Fact]
    public void Submit_ByNonHolder_ShouldThrow()
    {
        var p = ActivePolicy();
        var ex = Assert.Throws<InsuranceDomainException>(() =>
            Claim.Submit("CLM-1", p, "bob", 1_000_000m, new DateOnly(2026, 6, 1), "Nằm viện"));
        Assert.Equal("FORBIDDEN_POLICY", ex.ErrorCode);   // bob không phải chủ hợp đồng
    }

    [Fact]
    public void Approve_ShouldConsumeCoverage_AndAwaitPayout()
    {
        var p = ActivePolicy(coverage: 10_000_000m);
        var c = Claim.Submit("CLM-1", p, "alice", 4_000_000m, new DateOnly(2026, 6, 1), "Nằm viện");

        c.Approve("adjuster1", 3_000_000m, p);   // giám định cắt giảm còn 3tr

        Assert.Equal(ClaimStatus.Approved, c.Status);
        Assert.Equal(3_000_000m, c.ApprovedAmount);
        Assert.Equal(3_000_000m, p.ClaimedAmount);
        Assert.Equal(7_000_000m, p.RemainingCoverage);
    }

    [Fact]
    public void Approve_HigherThanRequested_ShouldThrow()
    {
        var p = ActivePolicy();
        var c = Claim.Submit("CLM-1", p, "alice", 1_000_000m, new DateOnly(2026, 6, 1), "Sự cố");

        var ex = Assert.Throws<InsuranceDomainException>(() => c.Approve("adjuster1", 2_000_000m, p));
        Assert.Equal("APPROVED_EXCEEDS_REQUESTED", ex.ErrorCode);
    }

    [Fact]
    public void SecondClaim_ExceedingRemainingCoverage_ShouldThrow()
    {
        var p = ActivePolicy(coverage: 10_000_000m);
        var first = Claim.Submit("CLM-1", p, "alice", 8_000_000m, new DateOnly(2026, 3, 1), "Lần 1");
        first.Approve("adjuster1", 8_000_000m, p);

        // Còn 2tr → yêu cầu 5tr phải bị chặn
        var ex = Assert.Throws<InsuranceDomainException>(() =>
            Claim.Submit("CLM-2", p, "alice", 5_000_000m, new DateOnly(2026, 6, 1), "Lần 2"));
        Assert.Equal("EXCEEDS_COVERAGE", ex.ErrorCode);
    }

    // ── Saga chi trả ─────────────────────────────────────────────
    [Fact]
    public void MarkPaid_OnlyAfterApproved_AndIdempotent()
    {
        var p = ActivePolicy();
        var c = Claim.Submit("CLM-1", p, "alice", 1_000_000m, new DateOnly(2026, 6, 1), "Sự cố");

        // Chưa duyệt mà báo đã trả → chặn
        Assert.Throws<InsuranceDomainException>(() => c.MarkPaid(Guid.NewGuid()));

        c.Approve("adjuster1", 1_000_000m, p);
        var transferId = Guid.NewGuid();
        c.MarkPaid(transferId);
        c.MarkPaid(Guid.NewGuid());   // message lặp → không đổi gì (idempotent)

        Assert.Equal(ClaimStatus.Paid, c.Status);
        Assert.Equal(transferId, c.PayoutTransferId);
    }

    [Fact]
    public void Reject_ShouldNotConsumeCoverage()
    {
        var p = ActivePolicy(coverage: 10_000_000m);
        var c = Claim.Submit("CLM-1", p, "alice", 4_000_000m, new DateOnly(2026, 6, 1), "Sự cố");

        c.Reject("adjuster1", "Không thuộc phạm vi bảo hiểm");

        Assert.Equal(ClaimStatus.Rejected, c.Status);
        Assert.Equal(0m, p.ClaimedAmount);            // hạn mức không bị trừ
        Assert.Equal(10_000_000m, p.RemainingCoverage);
    }
}

/// <summary>Công thức chi trả thật của ngành: miễn thường · đồng chi trả · thời gian chờ.</summary>
public class InsurancePayoutRulesTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    private static Policy Policy_(decimal coverage = 100_000_000m, decimal deductible = 0,
        decimal copay = 0, int waiting = 0)
    {
        var p = Policy.Issue("POL-1", "alice", "HEALTH", coverage, 2_000_000m, "ACC-001",
            From, To, "VND", deductible, copay, waiting);
        p.MarkPremiumCollected(Guid.NewGuid());
        return p;
    }

    [Fact]
    public void Payable_ShouldSubtractDeductible_ThenApplyCoPay()
    {
        // Viện phí 10tr, miễn thường 1tr, đồng chi trả 20% → BH trả (10−1)×0.8 = 7,2tr
        var p = Policy_(deductible: 1_000_000m, copay: 0.2m);
        Assert.Equal(7_200_000m, p.CalculatePayable(10_000_000m));
    }

    [Fact]
    public void Payable_CostBelowDeductible_ShouldBeZero()
    {
        var p = Policy_(deductible: 1_000_000m);
        Assert.Equal(0m, p.CalculatePayable(800_000m));   // dưới mức miễn thường → KH tự chịu
    }

    [Fact]
    public void Payable_ShouldBeCappedByRemainingCoverage()
    {
        var p = Policy_(coverage: 5_000_000m);
        Assert.Equal(5_000_000m, p.CalculatePayable(9_000_000m));
    }

    [Fact]
    public void Approve_ShouldConsumeCoverage_ByPayableNotAssessedCost()
    {
        var p = Policy_(coverage: 100_000_000m, deductible: 1_000_000m, copay: 0.2m);
        var c = Claim.Submit("CLM-1", p, "alice", 10_000_000m, new DateOnly(2026, 6, 1), "Nằm viện");

        c.Approve("adjuster", 10_000_000m, p);

        Assert.Equal(10_000_000m, c.AssessedCost);      // chi phí công nhận
        Assert.Equal(7_200_000m, c.ApprovedAmount);     // BH thực trả
        Assert.Equal(7_200_000m, p.ClaimedAmount);      // hạn mức trừ theo số thực trả
    }

    [Fact]
    public void Approve_NothingPayableAfterDeductible_ShouldThrow()
    {
        var p = Policy_(deductible: 5_000_000m);
        var c = Claim.Submit("CLM-1", p, "alice", 3_000_000m, new DateOnly(2026, 6, 1), "Sự cố nhỏ");

        var ex = Assert.Throws<InsuranceDomainException>(() => c.Approve("adjuster", 3_000_000m, p));
        Assert.Equal("NOTHING_PAYABLE", ex.ErrorCode);
    }

    [Fact]
    public void WaitingPeriod_IncidentTooEarly_ShouldThrow()
    {
        var p = Policy_(waiting: 30);   // 30 ngày chờ với bệnh thông thường
        var ex = Assert.Throws<InsuranceDomainException>(
            () => p.EnsureClaimable(1_000_000m, From.AddDays(10)));
        Assert.Equal("WITHIN_WAITING_PERIOD", ex.ErrorCode);
    }

    [Fact]
    public void WaitingPeriod_AfterElapsed_ShouldBeClaimable()
    {
        var p = Policy_(waiting: 30);
        p.EnsureClaimable(1_000_000m, From.AddDays(31));   // không ném lỗi
    }

    [Fact]
    public void PolicyOnlyActive_AfterPremiumCollected()
    {
        var p = Policy.Issue("POL-9", "alice", "HEALTH", 10_000_000m, 500_000m, "ACC-001", From, To);
        Assert.Equal(PolicyStatus.Draft, p.Status);       // chưa thu phí → chưa hiệu lực

        p.MarkPremiumCollected(Guid.NewGuid());
        Assert.Equal(PolicyStatus.Active, p.Status);

        p.MarkPremiumCollected(Guid.NewGuid());           // idempotent
        Assert.Equal(PolicyStatus.Active, p.Status);
    }
}

/// <summary>Hồi quy: số tiền chi trả phải là payable (sau miễn thường/đồng chi trả), KHÔNG phải chi phí công nhận.</summary>
public class ClaimPayoutAmountTests
{
    [Fact]
    public void ApprovedAmount_MustBePayable_NotAssessedCost()
    {
        var p = Policy.Issue("POL-1", "alice", "HEALTH", 100_000_000m, 2_000_000m, "ACC-1",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "VND",
            deductible: 1_000_000m, coPaymentRate: 0.2m);
        p.MarkPremiumCollected(Guid.NewGuid());

        var c = Claim.Submit("CLM-1", p, "alice", 10_000_000m, new DateOnly(2026, 6, 1), "Nằm viện");
        c.Approve("adjuster", 10_000_000m, p);

        // Event chi trả phải dùng giá trị này — chi 10tr là chi thừa 2,8tr.
        Assert.Equal(7_200_000m, c.ApprovedAmount);
        Assert.NotEqual(c.AssessedCost, c.ApprovedAmount);
    }
}

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
        p.Activate();
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

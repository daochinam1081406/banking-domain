using Accounts.Application;
using Accounts.Domain;
using Accounts.Domain.Exceptions;
using Xunit;

namespace BankingDomain.UnitTests;

/// <summary>Nghiệp vụ ngân hàng: quyền sở hữu · tiền tệ · bút toán kép.</summary>
public class BankingDomainRulesTests
{
    private static (MoneyTransferApplier Applier, Account From, Account To, FakeLedger Ledger)
        Build(string fromCcy = "VND", string toCcy = "VND", decimal balance = 1_000_000m)
    {
        var from = Account.Open("ACC-001", "alice", balance, fromCcy);
        var to = Account.Open("ACC-002", "bob", 0m, toCcy);
        var ledger = new FakeLedger();
        return (new MoneyTransferApplier(
            new FakeAccountRepository(from, to), new FakeInbox(), ledger, new FakeCache()), from, to, ledger);
    }

    // ── Ownership ────────────────────────────────────────────────
    [Fact]
    public void IsOwnedBy_ShouldMatchOnlyRealOwner()
    {
        var acc = Account.Open("ACC-001", "alice", 100m);

        Assert.True(acc.IsOwnedBy("alice"));
        Assert.False(acc.IsOwnedBy("bob"));      // bob KHÔNG được đụng tài khoản alice
    }

    [Fact]
    public void Open_WithoutOwner_ShouldThrow()
    {
        var ex = Assert.Throws<AccountsDomainException>(() => Account.Open("ACC-001", "", 100m));
        Assert.Equal("OWNER_REQUIRED", ex.ErrorCode);
    }

    // ── Currency ─────────────────────────────────────────────────
    [Fact]
    public void EnsureCurrency_Mismatch_ShouldThrow()
    {
        var usd = Account.Open("ACC-USD", "alice", 100m, "USD");

        var ex = Assert.Throws<AccountsDomainException>(() => usd.EnsureCurrency("VND"));
        Assert.Equal("CURRENCY_MISMATCH", ex.ErrorCode);
    }

    [Fact]
    public async Task Apply_CrossCurrency_ShouldFail_NoMoneyCreated()
    {
        var (applier, from, to, ledger) = Build(fromCcy: "VND", toCcy: "USD");

        var outcome = await applier.ApplyAsync("m1", Guid.NewGuid(), "ACC-001", "ACC-002", 50_000m, "VND");

        Assert.False(outcome.Applied);
        Assert.Equal("CURRENCY_MISMATCH", outcome.ErrorCode);
        Assert.Equal(1_000_000m, from.Balance);   // không trừ
        Assert.Equal(0m, to.Balance);             // không "tạo tiền" vào ví USD
        Assert.Empty(ledger.Entries);
    }

    // ── Double-entry ledger ──────────────────────────────────────
    [Fact]
    public async Task Apply_ShouldWriteBalancedDoubleEntry()
    {
        var (applier, _, _, ledger) = Build();
        var transferId = Guid.NewGuid();

        await applier.ApplyAsync("m1", transferId, "ACC-001", "ACC-002", 300_000m, "VND");

        Assert.Equal(2, ledger.Entries.Count);
        Assert.All(ledger.Entries, e => Assert.Equal(transferId, e.TransferId));

        var debit = ledger.Entries.Single(e => e.Direction == LedgerDirection.Debit);
        var credit = ledger.Entries.Single(e => e.Direction == LedgerDirection.Credit);

        Assert.Equal("ACC-001", debit.AccountNumber);
        Assert.Equal("ACC-002", credit.AccountNumber);
        Assert.Equal(debit.Amount, credit.Amount);       // tổng nợ = tổng có
        Assert.Equal(700_000m, debit.BalanceAfter);
        Assert.Equal(300_000m, credit.BalanceAfter);
    }

    [Fact]
    public async Task Apply_FailedTransfer_ShouldNotWriteLedger()
    {
        var (applier, _, _, ledger) = Build(balance: 10m);

        await applier.ApplyAsync("m1", Guid.NewGuid(), "ACC-001", "ACC-002", 5_000m, "VND");

        Assert.Empty(ledger.Entries);   // không có bút toán treo khi lệnh hỏng
    }
}

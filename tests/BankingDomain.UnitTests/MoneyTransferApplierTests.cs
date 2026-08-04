using Accounts.Application;
using Accounts.Domain;
using Xunit;

namespace BankingDomain.UnitTests;

// ── Fakes (không cần mocking lib) ───────────────────────────────
internal sealed class FakeAccountRepository(params Account[] accounts) : IAccountRepository
{
    private readonly List<Account> _accounts = [.. accounts];
    public int SaveCount { get; private set; }

    public Task<Account?> GetByNumberAsync(string number, CancellationToken ct = default)
        => Task.FromResult(_accounts.FirstOrDefault(a => a.Number == number.Trim().ToUpperInvariant()));

    public Task AddAsync(Account account, CancellationToken ct = default)
    {
        _accounts.Add(account);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeInbox : IInboxStore
{
    private readonly HashSet<string> _seen = [];
    public Task<bool> AlreadyProcessedAsync(string messageId, CancellationToken ct = default)
        => Task.FromResult(_seen.Contains(messageId));
    public void MarkProcessed(string messageId) => _seen.Add(messageId);
}

internal sealed class FakeLedger : ILedgerRepository
{
    public List<LedgerEntry> Entries { get; } = [];
    public void Add(LedgerEntry entry) => Entries.Add(entry);
    public Task<IReadOnlyList<LedgerEntryDto>> GetStatementAsync(
        string accountNumber, int limit = 50, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LedgerEntryDto>>([]);
}

internal sealed class FakeCache : IAccountCacheInvalidator
{
    public int Invalidations { get; private set; }
    public Task InvalidateAsync(string number, CancellationToken ct = default)
    {
        Invalidations++;
        return Task.CompletedTask;
    }
}

public class MoneyTransferApplierTests
{
    private static (MoneyTransferApplier Applier, Account From, Account To, FakeInbox Inbox, FakeLedger Ledger)
        Build(decimal fromBalance = 1_000_000m, string toCurrency = "VND")
    {
        var from = Account.Open("ACC-001", "owner1", fromBalance);
        var to = Account.Open("ACC-002", "owner1", 0m, toCurrency);
        var inbox = new FakeInbox();
        var ledger = new FakeLedger();
        var applier = new MoneyTransferApplier(
            new FakeAccountRepository(from, to), inbox, ledger, new FakeCache());
        return (applier, from, to, inbox, ledger);
    }

    [Fact]
    public async Task Apply_ShouldMoveMoney()
    {
        var (applier, from, to, _, ledger) = Build();

        var outcome = await applier.ApplyAsync("msg-1", Guid.NewGuid(), "ACC-001", "ACC-002", 300_000m, "VND");

        Assert.True(outcome.Applied);
        Assert.Equal(700_000m, from.Balance);
        Assert.Equal(300_000m, to.Balance);
    }

    [Fact]
    public async Task Apply_SameMessageTwice_ShouldNotDoubleSpend()
    {
        var (applier, from, to, _, ledger) = Build();

        var first = await applier.ApplyAsync("msg-1", Guid.NewGuid(), "ACC-001", "ACC-002", 300_000m, "VND");
        var second = await applier.ApplyAsync("msg-1", Guid.NewGuid(), "ACC-001", "ACC-002", 300_000m, "VND");

        Assert.True(first.Applied);
        Assert.True(second.Duplicate);
        Assert.False(second.Applied);
        Assert.Equal(700_000m, from.Balance);   // chỉ trừ 1 lần
        Assert.Equal(300_000m, to.Balance);
    }

    [Fact]
    public async Task Apply_DifferentMessages_ShouldBothApply()
    {
        var (applier, from, _, _, ledger) = Build();

        await applier.ApplyAsync("msg-1", Guid.NewGuid(), "ACC-001", "ACC-002", 100_000m, "VND");
        await applier.ApplyAsync("msg-2", Guid.NewGuid(), "ACC-001", "ACC-002", 100_000m, "VND");

        Assert.Equal(800_000m, from.Balance);
    }

    [Fact]
    public async Task Apply_InsufficientFunds_ShouldReturnFailedNotThrow()
    {
        var (applier, from, to, _, ledger) = Build(fromBalance: 100m);

        var outcome = await applier.ApplyAsync("msg-1", Guid.NewGuid(), "ACC-001", "ACC-002", 5_000m, "VND");

        Assert.False(outcome.Applied);
        Assert.False(outcome.Duplicate);
        Assert.Equal("INSUFFICIENT_FUNDS", outcome.ErrorCode);
        Assert.Equal(100m, from.Balance);   // không đổi
        Assert.Equal(0m, to.Balance);
    }

    [Fact]
    public async Task Apply_UnknownAccount_ShouldReturnFailed()
    {
        var (applier, _, _, _, ledger) = Build();

        var outcome = await applier.ApplyAsync("msg-1", Guid.NewGuid(), "ACC-999", "ACC-002", 1_000m, "VND");

        Assert.False(outcome.Applied);
        Assert.Equal("ACCOUNT_NOT_FOUND", outcome.ErrorCode);
    }

    [Fact]
    public async Task Apply_FailedTransfer_ShouldNotMarkInboxProcessed()
    {
        var (applier, _, _, inbox, ledger) = Build(fromBalance: 10m);

        await applier.ApplyAsync("msg-1", Guid.NewGuid(), "ACC-001", "ACC-002", 5_000m, "VND");

        // Không đánh dấu đã xử lý → không "nuốt" message nếu sau này retry hợp lệ
        Assert.False(await inbox.AlreadyProcessedAsync("msg-1"));
    }
}

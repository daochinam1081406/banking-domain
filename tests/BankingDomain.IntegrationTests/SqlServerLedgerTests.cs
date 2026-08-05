using Accounts.Application;
using Accounts.Domain;
using Accounts.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Xunit;


namespace BankingDomain.IntegrationTests;

/// <summary>
/// Chạy trên **SQL Server thật**. Kiểm những thứ chỉ lộ ở tầng DB:
///  • `RowVersion` (rowversion của SQL Server) có thật sự chặn lost update không
///  • Inbox dedup dựa vào **PRIMARY KEY**, không phải logic in-memory
///  • Bút toán kép ghi cùng transaction với số dư
/// </summary>
public sealed class SqlServerLedgerTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder().Build();
    private DbContextOptions<AccountsDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable) return;   // để Skip xử lý ở từng test
        await _sql.StartAsync();
        _options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseSqlServer(_sql.GetConnectionString()).Options;

        await using var db = new AccountsDbContext(_options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (DockerAvailability.IsAvailable) await _sql.DisposeAsync();
    }

    private AccountsDbContext NewDb() => new(_options);

    private async Task<(string From, string To)> SeedAccountsAsync(decimal balance = 10_000_000m)
    {
        await using var db = NewDb();
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var from = Account.Open($"F-{suffix}", "alice", balance);
        var to = Account.Open($"T-{suffix}", "alice", 0m);
        db.Accounts.AddRange(from, to);
        await db.SaveChangesAsync();
        return (from.Number, to.Number);
    }

    private static MoneyTransferApplier ApplierFor(AccountsDbContext db)
        => new(new EfAccountRepository(db), new EfInboxStore(db),
               new EfLedgerRepository(db), new NoOpAccountCacheInvalidator());

    [SkippableFact]
    public async Task Apply_ShouldPersistBalances_AndDoubleEntryLedger()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);

        var (from, to) = await SeedAccountsAsync();
        var transferId = Guid.NewGuid();

        await using (var db = NewDb())
            await ApplierFor(db).ApplyAsync("msg-1", transferId, from, to, 300_000m, "VND");

        await using var verify = NewDb();
        Assert.Equal(9_700_000m, (await verify.Accounts.SingleAsync(a => a.Number == from)).Balance);
        Assert.Equal(300_000m, (await verify.Accounts.SingleAsync(a => a.Number == to)).Balance);

        var entries = await verify.LedgerEntries.Where(l => l.TransferId == transferId).ToListAsync();
        Assert.Equal(2, entries.Count);   // bút toán kép
        Assert.Equal(entries.Single(e => e.Direction == LedgerDirection.Debit).Amount,
                     entries.Single(e => e.Direction == LedgerDirection.Credit).Amount);
    }

    /// <summary>Message lặp (broker at-least-once) — inbox PK phải chặn trừ tiền lần 2.</summary>
    [SkippableFact]
    public async Task Apply_DuplicateMessage_ShouldNotDoubleSpend()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);

        var (from, to) = await SeedAccountsAsync();

        await using (var db = NewDb())
            await ApplierFor(db).ApplyAsync("dup-1", Guid.NewGuid(), from, to, 500_000m, "VND");

        // DbContext MỚI ⇒ không có state in-memory, chỉ còn dựa vào bảng inbox trong DB
        await using (var db2 = NewDb())
        {
            var outcome = await ApplierFor(db2).ApplyAsync("dup-1", Guid.NewGuid(), from, to, 500_000m, "VND");
            Assert.True(outcome.Duplicate);
        }

        await using var verify = NewDb();
        Assert.Equal(9_500_000m, (await verify.Accounts.SingleAsync(a => a.Number == from)).Balance);
        Assert.Single(await verify.LedgerEntries.Where(l => l.AccountNumber == from).ToListAsync());
    }

    /// <summary>RowVersion phải ném DbUpdateConcurrencyException khi 2 context cùng sửa 1 account.</summary>
    [SkippableFact]
    public async Task ConcurrentUpdate_ShouldThrowConcurrencyException()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);

        var (from, _) = await SeedAccountsAsync();

        await using var db1 = NewDb();
        await using var db2 = NewDb();
        var a1 = await db1.Accounts.SingleAsync(a => a.Number == from);
        var a2 = await db2.Accounts.SingleAsync(a => a.Number == from);

        a1.Debit(100_000m);
        await db1.SaveChangesAsync();      // ghi trước → RowVersion đổi

        a2.Debit(200_000m);                // db2 giữ RowVersion cũ
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Apply_InsufficientFunds_ShouldNotWriteLedgerOrInbox()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);

        var (from, to) = await SeedAccountsAsync(balance: 100m);

        await using (var db = NewDb())
        {
            var outcome = await ApplierFor(db).ApplyAsync("fail-1", Guid.NewGuid(), from, to, 5_000m, "VND");
            Assert.Equal("INSUFFICIENT_FUNDS", outcome.ErrorCode);
        }

        await using var verify = NewDb();
        Assert.Equal(100m, (await verify.Accounts.SingleAsync(a => a.Number == from)).Balance);
        Assert.Empty(await verify.LedgerEntries.Where(l => l.AccountNumber == from).ToListAsync());
        Assert.False(await verify.ProcessedMessages.AnyAsync(p => p.MessageId == "fail-1"));
    }
}

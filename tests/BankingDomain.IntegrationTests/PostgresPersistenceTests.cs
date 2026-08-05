using BuildingBlocks.Contracts;
using Dapper;
using Npgsql;
using Payments.Application;
using Payments.Domain;
using Payments.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;


namespace BankingDomain.IntegrationTests;

/// <summary>
/// Chạy trên **PostgreSQL thật** (Testcontainers). Những lỗi dưới đây unit test KHÔNG bắt được vì
/// chúng chỉ lộ khi có driver + kiểu dữ liệu thật:
///  • Npgsql trả `DateTime` cho `timestamptz` ⇒ Dapper không match ctor record dùng `DateTimeOffset`
///  • Outbox + transfer phải commit CÙNG transaction
///  • `FOR UPDATE SKIP LOCKED` để publisher an toàn multi-instance
/// </summary>
public sealed class PostgresPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("payments")
        .Build();

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable) return;   // để Skip xử lý ở từng test
        await _pg.StartAsync();
        PersistenceConventions.Apply();
        _dataSource = NpgsqlDataSource.Create(_pg.GetConnectionString());
        await SchemaInitializer.EnsureCreatedAsync(_dataSource);
    }

    public async Task DisposeAsync()
    {
        if (!DockerAvailability.IsAvailable) return;
        await _dataSource.DisposeAsync();
        await _pg.DisposeAsync();
    }

    private static MoneyTransferredIntegrationEvent EventFor(Transfer t) => new()
    {
        TransferId = t.Id, FromAccount = t.FromAccount, ToAccount = t.ToAccount,
        Amount = t.Amount, Currency = t.Currency,
    };

    [SkippableFact]
    public async Task SaveWithOutbox_ShouldWriteTransferAndOutbox_InSameTransaction()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);

        var repo = new DapperTransferRepository(_dataSource);
        var transfer = Transfer.Initiate("ACC-001", "ACC-002", 500_000m);

        await repo.SaveWithOutboxAsync(transfer, EventFor(transfer));

        await using var conn = await _dataSource.OpenConnectionAsync();
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM transfers WHERE id = @Id", new { transfer.Id }));
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM outbox WHERE status = 'PENDING'"));
    }

    /// <summary>Hồi quy: DTO có DateTimeOffset đọc từ timestamptz — từng gây 500 trên môi trường thật.</summary>
    [SkippableFact]
    public async Task ReadTransfer_ShouldMapDateTimeOffset_FromTimestamptz()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);

        var repo = new DapperTransferRepository(_dataSource);
        var reads = new DapperTransferReadService(_dataSource);
        var transfer = Transfer.Initiate("ACC-001", "ACC-002", 250_000m);
        await repo.SaveWithOutboxAsync(transfer, EventFor(transfer));

        var dto = await reads.GetByIdAsync(transfer.Id);

        Assert.NotNull(dto);
        Assert.Equal("Initiated", dto!.Status);
        Assert.NotEqual(default, dto.CreatedAt);
        Assert.Null(dto.CompletedAt);        // cột nullable timestamptz cũng phải map được
    }

    [SkippableFact]
    public async Task StatusWriter_ShouldCloseTransfer_AndBeIdempotent()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);

        var repo = new DapperTransferRepository(_dataSource);
        var writer = new DapperTransferStatusWriter(_dataSource);
        var reads = new DapperTransferReadService(_dataSource);
        var transfer = Transfer.Initiate("ACC-001", "ACC-002", 700_000m);
        await repo.SaveWithOutboxAsync(transfer, EventFor(transfer));

        await writer.MarkCompletedAsync(transfer.Id);
        await writer.MarkFailedAsync(transfer.Id, "X", "không được ghi đè");   // đã Completed → bỏ qua

        var dto = await reads.GetByIdAsync(transfer.Id);
        Assert.Equal("Completed", dto!.Status);
        Assert.Null(dto.FailureCode);
        Assert.NotNull(dto.CompletedAt);
    }

    /// <summary>Outbox phải dùng SKIP LOCKED để 2 publisher không lấy trùng row.</summary>
    [SkippableFact]
    public async Task OutboxQuery_WithSkipLocked_ShouldNotReturnRowsLockedByAnother()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);

        var repo = new DapperTransferRepository(_dataSource);
        var transfer = Transfer.Initiate("ACC-001", "ACC-002", 100_000m);
        await repo.SaveWithOutboxAsync(transfer, EventFor(transfer));

        const string sql = """
            SELECT id FROM outbox WHERE status = 'PENDING'
            ORDER BY created_at LIMIT 10 FOR UPDATE SKIP LOCKED
            """;

        await using var connA = await _dataSource.OpenConnectionAsync();
        await using var txA = await connA.BeginTransactionAsync();
        var rowsA = (await connA.QueryAsync<Guid>(sql, transaction: txA)).ToList();
        Assert.Single(rowsA);   // publisher A giữ row

        await using var connB = await _dataSource.OpenConnectionAsync();
        await using var txB = await connB.BeginTransactionAsync();
        var rowsB = (await connB.QueryAsync<Guid>(sql, transaction: txB)).ToList();
        Assert.Empty(rowsB);    // publisher B bỏ qua, không xử lý trùng

        await txA.RollbackAsync();
        await txB.RollbackAsync();
    }

    /// <summary>
    /// Hồi quy cho lỗi chi tiền hai lần.
    ///
    /// Service Bus chỉ bảo đảm at-least-once: cùng một `ClaimApproved` có thể được giao lại
    /// (Complete lỗi, lease hết hạn, service restart giữa chừng). Trước khi có inbox,
    /// `ClaimPayoutConsumer` gọi `Transfer.Initiate()` mỗi lần nhận ⇒ mỗi lần giao lại là một lệnh
    /// chi tiền mới cho cùng một hồ sơ bồi thường.
    ///
    /// Test dùng **cùng messageId, khác Transfer** — đúng như lúc chạy thật, vì consumer sinh
    /// Transfer mới mỗi lần. Chốt chặn phải là PRIMARY KEY của bảng inbox, không phải trùng Transfer.Id.
    /// </summary>
    [SkippableFact]
    public async Task SaveWithOutbox_RedeliveredMessage_ShouldNotPayTwice()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);

        var repo = new DapperTransferRepository(_dataSource);
        const string messageId = "claim-approved-msg-1";

        var first = Transfer.Initiate("INS-FUND", "ACC-900", 7_200_000m, "VND");
        var appliedFirst = await repo.SaveWithOutboxAsync(first, [EventFor(first)], messageId);

        // Broker giao lại: consumer sinh Transfer HOÀN TOÀN MỚI, chỉ messageId là trùng.
        var duplicate = Transfer.Initiate("INS-FUND", "ACC-900", 7_200_000m, "VND");
        var appliedSecond = await repo.SaveWithOutboxAsync(duplicate, [EventFor(duplicate)], messageId);

        Assert.True(appliedFirst);
        Assert.False(appliedSecond);

        await using var conn = await _dataSource.OpenConnectionAsync();
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM transfers WHERE to_account = 'ACC-900'"));
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM transfers WHERE id = @Id", new { duplicate.Id }));
        // Lệnh chi thứ hai không được lọt vào outbox, nếu không Accounts vẫn ghi nợ lần nữa.
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM outbox WHERE payload->>'ToAccount' = 'ACC-900'"));
    }

    /// <summary>
    /// Transfer + mọi outbox event + dấu inbox phải nằm trong CÙNG một transaction.
    /// Nếu sự kiện báo kết quả saga (`ClaimPayoutCompleted`) publish riêng bên ngoài, crash vào đúng
    /// khe giữa hai bước sẽ chuyển tiền xong mà bên bảo hiểm không bao giờ biết ⇒ hồ sơ kẹt ở Approved.
    /// </summary>
    [SkippableFact]
    public async Task SaveWithOutbox_ShouldWriteAllEvents_Atomically()
    {
        Skip.IfNot(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);

        var repo = new DapperTransferRepository(_dataSource);
        var transfer = Transfer.Initiate("INS-FUND", "ACC-901", 5_000_000m, "VND");
        var claimId = Guid.NewGuid();

        var applied = await repo.SaveWithOutboxAsync(
            transfer,
            [
                EventFor(transfer),
                new ClaimPayoutCompletedIntegrationEvent { ClaimId = claimId, TransferId = transfer.Id },
            ],
            "claim-approved-msg-2");

        Assert.True(applied);

        await using var conn = await _dataSource.OpenConnectionAsync();
        var types = (await conn.QueryAsync<string>(
            "SELECT event_type FROM outbox WHERE payload->>'TransferId' = @Id",
            new { Id = transfer.Id.ToString() })).ToList();

        Assert.Contains(nameof(MoneyTransferredIntegrationEvent), types);
        Assert.Contains(nameof(ClaimPayoutCompletedIntegrationEvent), types);
    }
}

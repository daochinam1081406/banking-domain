using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Payments.Infrastructure;

/// <summary>
/// Poll outbox → publish lên broker → mark PUBLISHED. <c>FOR UPDATE SKIP LOCKED</c> cho phép
/// chạy nhiều instance. At-least-once: crash sau khi publish nhưng trước khi update thì message
/// được gửi lại, consumer khử trùng theo MessageId.
///
/// Hút đầy batch thì lặp lại ngay, chỉ ngủ khi hàng đợi cạn — nhịp đẩy co giãn theo tải thay vì
/// bị chặn bởi chu kỳ poll cố định (nhịp đẩy chậm hơn nhịp ghi = hàng đợi phình không giới hạn).
/// </summary>
public sealed class OutboxPublisher(
    NpgsqlDataSource dataSource,
    IEventBus eventBus,
    IEventStreamPublisher eventStream,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private const int BatchSize = 200;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);

    private sealed record OutboxRow(Guid Id, string EventType, string Payload, string? CorrelationId, int? SchemaVersion);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var drained = 0;
            try { drained = await PublishPendingAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Outbox publish loop error"); }

            // Còn tồn đọng (batch đầy) → hút tiếp ngay, không ngủ.
            if (drained == BatchSize) continue;
            await Task.Delay(IdleDelay, stoppingToken);
        }
    }

    /// <returns>Số event đã đẩy — quyết định có hút tiếp ngay hay không.</returns>
    private async Task<int> PublishPendingAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var rows = (await conn.QueryAsync<OutboxRow>(new CommandDefinition(
            """
            SELECT id AS Id, event_type AS EventType, payload AS Payload, correlation_id AS CorrelationId,
                   schema_version AS SchemaVersion
            FROM outbox
            WHERE status = 'PENDING'
            ORDER BY created_at
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED
            """, new { BatchSize }, tx, cancellationToken: ct))).ToList();

        if (rows.Count == 0)
        {
            await tx.CommitAsync(ct);
            return 0;
        }

        // Correlation đi kèm từng message: gửi theo lô thì không còn một "message đang xử lý"
        // duy nhất để gắn vào AsyncLocal.
        var batch = rows
            .Select(r => new OutboxMessage(
                r.EventType, r.Payload, r.Id.ToString(), r.CorrelationId, r.SchemaVersion ?? 1))
            .ToList();

        var swBus = System.Diagnostics.Stopwatch.StartNew();
        await eventBus.PublishBatchAsync(batch, ct);
        swBus.Stop();

        // Kafka là kênh stream song song (audit/analytics, replay được), không thay Service Bus.
        var swStream = System.Diagnostics.Stopwatch.StartNew();
        await eventStream.PublishBatchAsync(batch, ct);
        swStream.Stop();

        // Một UPDATE cho cả lô: batch 200 thì bớt 199 round-trip tới DB.
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE outbox SET status = 'PUBLISHED', published_at = NOW() WHERE id = ANY(@Ids)",
            new { Ids = rows.Select(r => r.Id).ToArray() }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        // Tách thời gian từng kênh để biết ngay nghẽn ở broker nào khi outbox chậm.
        logger.LogInformation(
            "Outbox published {Count} event(s) — ServiceBus {BusMs}ms · Kafka {StreamMs}ms",
            rows.Count, swBus.ElapsedMilliseconds, swStream.ElapsedMilliseconds);
        return rows.Count;
    }
}

using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Payments.Infrastructure;

/// <summary>
/// Poll outbox mỗi 1s → publish lên Service Bus qua IEventBus → mark PUBLISHED.
/// FOR UPDATE SKIP LOCKED → an toàn multi-instance. Đảm bảo at-least-once: nếu publish xong mà
/// crash trước khi update, message được publish lại (consumer idempotent theo MessageId).
/// </summary>
public sealed class OutboxPublisher(
    NpgsqlDataSource dataSource,
    IEventBus eventBus,
    IEventStreamPublisher eventStream,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private sealed record OutboxRow(Guid Id, string EventType, string Payload, string? CorrelationId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PublishPendingAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Outbox publish loop error"); }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task PublishPendingAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var rows = (await conn.QueryAsync<OutboxRow>(new CommandDefinition(
            """
            SELECT id AS Id, event_type AS EventType, payload AS Payload, correlation_id AS CorrelationId
            FROM outbox
            WHERE status = 'PENDING'
            ORDER BY created_at
            LIMIT 20
            FOR UPDATE SKIP LOCKED
            """, transaction: tx, cancellationToken: ct))).ToList();

        foreach (var row in rows)
        {
            // Khôi phục correlation của request gốc → message mang đúng id, log nối được đầu-cuối.
            CorrelationContext.Set(row.CorrelationId ?? Guid.NewGuid().ToString("N"));
            using var logScope = logger.BeginScope(new Dictionary<string, object>
            {
                [CorrelationContext.LogPropertyName] = CorrelationContext.Id ?? "-",
            });
            await eventBus.PublishRawAsync(row.EventType, row.Payload, row.Id.ToString(), ct);

            // Song song: đẩy sang Kafka làm event stream (audit/analytics, replay được).
            // Service Bus lo giao dịch/saga; Kafka lo streaming — hai vai trò khác nhau.
            await eventStream.PublishAsync(row.EventType, row.Payload, row.Id.ToString(), ct);
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE outbox SET status = 'PUBLISHED', published_at = NOW() WHERE id = @Id",
                new { row.Id }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        if (rows.Count > 0) logger.LogInformation("Outbox published {Count} event(s)", rows.Count);
    }
}

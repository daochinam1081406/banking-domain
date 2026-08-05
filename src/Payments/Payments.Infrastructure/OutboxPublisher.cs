using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Payments.Infrastructure;

/// <summary>
/// Poll outbox → publish lên Service Bus qua IEventBus → mark PUBLISHED.
/// FOR UPDATE SKIP LOCKED → an toàn multi-instance. Đảm bảo at-least-once: nếu publish xong mà
/// crash trước khi update, message được publish lại (consumer idempotent theo MessageId).
///
/// **Nhịp đẩy phải theo kịp nhịp ghi.** Bản đầu dùng `LIMIT 20` + ngủ cố định 1 giây ⇒ trần cứng
/// 20 event/giây, trong khi API nhận ~364 lệnh/giây (đo bằng load test). Chênh ~18 lần nghĩa là
/// dưới tải thật hàng đợi phình vô hạn và saga chậm dần không giới hạn — outbox vẫn "đúng" nhưng
/// vô dụng vì độ trễ không chặn được.
///
/// Sửa: (1) batch lớn hơn; (2) **drain mode** — hút được đầy batch thì lặp lại NGAY, chỉ ngủ khi
/// hàng đợi cạn, nên nhịp đẩy tự co giãn theo tải thay vì bị khoá bởi hằng số.
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

    /// <returns>Số event đã đẩy trong vòng này — dùng để quyết định có hút tiếp ngay hay không.</returns>
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

        // Correlation đi kèm TỪNG message thay vì đặt vào AsyncLocal trước mỗi lần gửi —
        // gửi theo lô thì không còn "message đang xử lý" duy nhất để gắn context vào.
        var batch = rows
            .Select(r => new OutboxMessage(
                r.EventType, r.Payload, r.Id.ToString(), r.CorrelationId, r.SchemaVersion ?? 1))
            .ToList();

        var swBus = System.Diagnostics.Stopwatch.StartNew();
        await eventBus.PublishBatchAsync(batch, ct);
        swBus.Stop();

        // Song song: đẩy sang Kafka làm event stream (audit/analytics, replay được).
        // Service Bus lo giao dịch/saga; Kafka lo streaming — hai vai trò khác nhau.
        var swStream = System.Diagnostics.Stopwatch.StartNew();
        await eventStream.PublishBatchAsync(batch, ct);
        swStream.Stop();

        // Một UPDATE cho cả batch thay vì mỗi dòng một lệnh — batch 200 thì tiết kiệm 199 vòng
        // round-trip tới DB. Ngữ nghĩa không đổi: trước đây các UPDATE cũng chỉ commit ở cuối tx.
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE outbox SET status = 'PUBLISHED', published_at = NOW() WHERE id = ANY(@Ids)",
            new { Ids = rows.Select(r => r.Id).ToArray() }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        // Tách riêng thời gian từng kênh: khi outbox chậm, cần biết ngay nghẽn ở broker nào
        // thay vì phải đoán từ dấu thời gian của log.
        logger.LogInformation(
            "Outbox published {Count} event(s) — ServiceBus {BusMs}ms · Kafka {StreamMs}ms",
            rows.Count, swBus.ElapsedMilliseconds, swStream.ElapsedMilliseconds);
        return rows.Count;
    }
}

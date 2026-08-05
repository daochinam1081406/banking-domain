using BuildingBlocks.Observability;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Nền cho Kafka consumer: **consumer group** (scale ngang — mỗi partition chỉ 1 consumer trong group),
/// **manual offset commit** (chỉ commit sau khi xử lý xong → at-least-once, không mất message khi crash),
/// và log partition/offset để trace.
///
/// Khác Service Bus: Kafka giữ message theo retention (replay được), ordering đảm bảo **trong 1 partition**
/// nên key phải chọn sao cho các message cần thứ tự rơi cùng partition (ở đây key = accountNumber/messageId).
/// </summary>
public abstract class KafkaConsumerBase(
    IOptions<KafkaOptions> options,
    ILogger logger) : BackgroundService
{
    protected abstract string ConsumerGroup { get; }

    /// <summary>Xử lý 1 message. Ném exception ⇒ KHÔNG commit offset ⇒ message được giao lại.</summary>
    protected abstract Task HandleAsync(
        string eventType, string payload, string? correlationId, CancellationToken ct);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        // Consume loop là blocking → chạy trên thread riêng để không chặn host startup.
        => Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);

    private async Task ConsumeLoop(CancellationToken ct)
    {
        var opt = options.Value;
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = opt.BootstrapServers,
            GroupId = ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,   // service mới join vẫn đọc được lịch sử
            EnableAutoCommit = false,                     // commit tay sau khi xử lý xong
        })
        .SetErrorHandler((_, e) => logger.LogWarning("Kafka error: {Reason}", e.Reason))
        .SetPartitionsAssignedHandler((_, parts) =>
            logger.LogInformation("Kafka group {Group} nhận partitions: {Partitions}",
                ConsumerGroup, string.Join(",", parts.Select(p => p.Partition.Value))))
        .Build();

        // Chờ broker sẵn sàng rồi mới subscribe (container start ≠ Kafka ready).
        for (var attempt = 1; !ct.IsCancellationRequested; attempt++)
        {
            try { consumer.Subscribe(opt.Topic); break; }
            catch (Exception ex) when (attempt < 20)
            {
                logger.LogDebug(ex, "Kafka chưa sẵn sàng, thử lại lần {Attempt}", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
        logger.LogInformation("Kafka consumer {Group} đang đọc topic {Topic}", ConsumerGroup, opt.Topic);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(1));
                if (result?.Message is null) continue;

                var eventType = GetHeader(result.Message, "eventType") ?? "unknown";
                var correlationId = GetHeader(result.Message, CorrelationContext.MessagePropertyName);
                if (!string.IsNullOrWhiteSpace(correlationId)) CorrelationContext.Set(correlationId);

                await HandleAsync(eventType, result.Message.Value, correlationId, ct);

                consumer.Commit(result);   // chỉ commit khi đã xử lý xong
                logger.LogInformation(
                    "Kafka consumed {EventType} từ partition {Partition} offset {Offset}",
                    eventType, result.Partition.Value, result.Offset.Value);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Không commit → Kafka giao lại message này ở lần poll sau.
                logger.LogError(ex, "Kafka consumer {Group} lỗi khi xử lý — sẽ nhận lại message", ConsumerGroup);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        consumer.Close();   // rời group gọn gàng → rebalance ngay, không chờ session timeout
    }

    private static string? GetHeader(Message<string, string> message, string key)
        => message.Headers.TryGetLastBytes(key, out var bytes)
            ? System.Text.Encoding.UTF8.GetString(bytes)
            : null;
}

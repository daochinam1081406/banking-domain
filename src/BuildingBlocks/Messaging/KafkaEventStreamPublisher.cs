using System.Text;
using Confluent.Kafka;
using BuildingBlocks.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Producer Kafka cho luồng streaming. `key` quyết định partition ⇒ mọi event của cùng một
/// tài khoản/hợp đồng rơi vào cùng partition ⇒ **đảm bảo thứ tự** cho thực thể đó.
/// Idempotent producer + Acks.All để không mất/không nhân đôi khi retry.
/// </summary>
public sealed class KafkaEventStreamPublisher : IEventStreamPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private readonly ILogger<KafkaEventStreamPublisher> _logger;

    public KafkaEventStreamPublisher(IOptions<KafkaOptions> options, ILogger<KafkaEventStreamPublisher> logger)
    {
        var opt = options.Value;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers  = opt.BootstrapServers,
            Acks              = Acks.All,
            EnableIdempotence = true,
        }).Build();
        _topic = opt.Topic;
        _logger = logger;
    }

    public async Task PublishAsync(string eventType, string jsonPayload, string key, CancellationToken ct = default)
    {
        try
        {
            var result = await _producer.ProduceAsync(_topic, new Message<string, string>
            {
                Key = key,
                Value = jsonPayload,
                Headers = new Headers
                {
                    { "eventType", Encoding.UTF8.GetBytes(eventType) },
                    { CorrelationContext.MessagePropertyName, Encoding.UTF8.GetBytes(CorrelationContext.GetOrCreate()) },
                },
            }, ct);

            _logger.LogInformation("Kafka stream {EventType} → partition {Partition} offset {Offset}",
                eventType, result.Partition.Value, result.Offset.Value);
        }
        catch (Exception ex)
        {
            // Stream analytics là best-effort — hỏng không được làm chết luồng giao dịch chính.
            _logger.LogError(ex, "Không stream được {EventType} sang Kafka", eventType);
        }
    }

    /// <summary>
    /// `Produce` (không await) chỉ xếp message vào hàng đợi nội bộ của librdkafka — nó tự gộp
    /// thành ít request tới broker. Chỉ `Flush` một lần ở cuối để chờ toàn bộ delivery report.
    /// Khác hẳn `ProduceAsync` từng message: cách đó chờ ack cho mỗi message, biến độ trễ mạng
    /// thành số nhân trên toàn lô.
    /// </summary>
    public Task PublishBatchAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) return Task.CompletedTask;

        var failed = 0;
        foreach (var m in messages)
        {
            try
            {
                _producer.Produce(_topic, new Message<string, string>
                {
                    Key = m.MessageId,
                    Value = m.JsonPayload,
                    Headers = new Headers
                    {
                        { "eventType", Encoding.UTF8.GetBytes(m.EventType) },
                        { CorrelationContext.MessagePropertyName,
                          Encoding.UTF8.GetBytes(m.CorrelationId ?? Guid.NewGuid().ToString("N")) },
                    },
                }, report => { if (report.Error.IsError) Interlocked.Increment(ref failed); });
            }
            catch (ProduceException<string, string> ex)
            {
                // Stream là best-effort — hàng đợi đầy hoặc broker lỗi không được làm chết luồng giao dịch.
                _logger.LogError(ex, "Không xếp được {EventType} vào hàng đợi Kafka", m.EventType);
            }
        }

        _producer.Flush(ct);
        if (failed > 0) _logger.LogWarning("{Failed}/{Total} message không stream được sang Kafka", failed, messages.Count);
        else _logger.LogInformation("Streamed {Count} event(s) → Kafka theo lô", messages.Count);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

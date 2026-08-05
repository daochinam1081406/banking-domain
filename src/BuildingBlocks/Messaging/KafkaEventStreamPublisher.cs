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

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

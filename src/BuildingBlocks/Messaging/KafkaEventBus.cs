using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "banking-events";
}

/// <summary>IEventBus impl trên Kafka — key = messageId (ordering/partition), header eventType.</summary>
public sealed class KafkaEventBus : IEventBus, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private readonly ILogger<KafkaEventBus> _logger;

    public KafkaEventBus(IOptions<KafkaOptions> options, ILogger<KafkaEventBus> logger)
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

    public Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : IntegrationEvent
        => PublishRawAsync(@event.EventType,
            JsonSerializer.Serialize(@event, @event.GetType()), @event.EventId.ToString(), ct);

    public async Task PublishRawAsync(string eventType, string jsonPayload, string messageId, CancellationToken ct = default)
    {
        var message = new Message<string, string>
        {
            Key = messageId,
            Value = jsonPayload,
            Headers = new Headers { { "eventType", Encoding.UTF8.GetBytes(eventType) } },
        };
        var result = await _producer.ProduceAsync(_topic, message, ct);
        _logger.LogInformation("Published {EventType} {MessageId} → Kafka {Offset}", eventType, messageId, result.TopicPartitionOffset);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

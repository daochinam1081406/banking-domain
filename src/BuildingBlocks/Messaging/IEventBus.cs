using BuildingBlocks.Contracts;
namespace BuildingBlocks.Messaging;

/// <summary>Abstraction cho message broker — đổi Service Bus/Kafka/Event Grid không đụng domain.</summary>
public interface IEventBus
{
    Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : IntegrationEvent;

    /// <summary>Payload đã serialize sẵn — dùng bởi OutboxPublisher.</summary>
    Task PublishRawAsync(string eventType, string jsonPayload, string messageId,
        int schemaVersion = 1, CancellationToken ct = default);

    /// <summary>
    /// Mặc định lặp tuần tự; impl có API batch nên override — gửi từng message rồi await
    /// khiến nhịp đẩy outbox bị chặn bởi độ trễ mạng thay vì CPU hay DB.
    /// </summary>
    async Task PublishBatchAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default)
    {
        foreach (var m in messages)
            await PublishRawAsync(m.EventType, m.JsonPayload, m.MessageId, m.SchemaVersion, ct);
    }
}

public sealed record OutboxMessage(
    string EventType, string JsonPayload, string MessageId, string? CorrelationId, int SchemaVersion);

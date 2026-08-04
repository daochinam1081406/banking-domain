namespace BuildingBlocks.Messaging;

/// <summary>
/// Abstraction cho message broker — impl hiện tại: Azure Service Bus.
/// Đổi sang Kafka/Event Grid chỉ cần thay implementation, không đụng domain.
/// </summary>
public interface IEventBus
{
    /// <summary>Publish 1 event đã typed (dùng trực tiếp trong handler nếu không qua Outbox).</summary>
    Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : IntegrationEvent;

    /// <summary>Publish payload đã serialize sẵn (dùng bởi OutboxPublisher — payload lấy từ outbox table).</summary>
    Task PublishRawAsync(string eventType, string jsonPayload, string messageId, CancellationToken ct = default);
}

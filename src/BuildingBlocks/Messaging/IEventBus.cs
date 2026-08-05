using BuildingBlocks.Contracts;
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
    Task PublishRawAsync(string eventType, string jsonPayload, string messageId,
        int schemaVersion = 1, CancellationToken ct = default);

    /// <summary>
    /// Publish cả lô trong tối thiểu số lần đi-về mạng.
    /// Gửi từng message rồi await là nút thắt lớn nhất của outbox: mỗi message một round-trip,
    /// nên tốc độ đẩy bị chặn bởi độ trễ mạng chứ không phải bởi CPU hay DB.
    /// Mặc định ở đây vẫn là vòng lặp (đúng nhưng chậm) để impl nào không hỗ trợ batch vẫn chạy;
    /// impl nào có API batch thật thì override.
    /// </summary>
    async Task PublishBatchAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default)
    {
        foreach (var m in messages)
            await PublishRawAsync(m.EventType, m.JsonPayload, m.MessageId, m.SchemaVersion, ct);
    }
}

/// <summary>Một message lấy từ bảng outbox, mang sẵn correlation riêng của request gốc.</summary>
public sealed record OutboxMessage(
    string EventType, string JsonPayload, string MessageId, string? CorrelationId, int SchemaVersion);

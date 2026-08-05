namespace Accounts.Domain;

/// <summary>
/// Bản ghi audit dựng từ event stream Kafka (không phải từ Service Bus) — minh hoạ đúng vai trò
/// của Kafka: pipeline sự kiện để audit/analytics, replay được từ đầu topic khi cần dựng lại.
/// </summary>
public sealed class AuditEvent
{
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = null!;
    public string? CorrelationId { get; private set; }
    public string Payload { get; private set; } = null!;
    public DateTimeOffset ReceivedAt { get; private set; }

    private AuditEvent() { }

    public static AuditEvent From(string eventType, string payload, string? correlationId) => new()
    {
        Id = Guid.NewGuid(),
        EventType = eventType,
        CorrelationId = correlationId,
        Payload = payload.Length > 4000 ? payload[..4000] : payload,
        ReceivedAt = DateTimeOffset.UtcNow,
    };
}

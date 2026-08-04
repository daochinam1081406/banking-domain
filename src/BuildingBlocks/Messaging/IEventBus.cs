namespace BuildingBlocks.Messaging;

/// <summary>
/// Abstraction cho message broker — impl hiện tại: Azure Service Bus.
/// Đổi sang Kafka/Event Grid chỉ cần thay implementation, không đụng domain.
/// </summary>
public interface IEventBus
{
    Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : IntegrationEvent;
}

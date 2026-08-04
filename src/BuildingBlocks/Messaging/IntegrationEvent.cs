namespace BuildingBlocks.Messaging;

/// <summary>Base cho integration event truyền giữa các microservice qua message broker.</summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string EventType => GetType().Name;
}

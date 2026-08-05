namespace BuildingBlocks.Contracts;

/// <summary>
/// Base cho integration event. Nằm ở project **BuildingBlocks.Contracts** — assembly *thuần khai báo*,
/// KHÔNG phụ thuộc gói nào (không Azure SDK, không EF, không ASP.NET).
///
/// Vì sao tách riêng: contract là thứ mọi service **buộc** phải chia sẻ; implementation (Service Bus,
/// Redis, Auth…) thì không. Gộp chung ⇒ sửa 1 dòng hạ tầng phải rebuild + redeploy TOÀN BỘ service,
/// mất đúng lợi ích lớn nhất của microservices là deploy độc lập.
/// Assembly này versioned riêng (nếu đóng NuGet) và đổi rất chậm — đúng bản chất của contract.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string EventType => GetType().Name;
}

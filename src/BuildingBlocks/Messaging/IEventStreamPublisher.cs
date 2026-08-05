using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Kênh **event streaming** (Kafka) — tách khỏi `IEventBus` (Azure Service Bus) một cách có chủ đích:
///
///  • **Service Bus** = messaging giao dịch: saga/command, cần DLQ, ordering theo session, at-least-once
///    với retry/abandon rõ ràng. Message tiêu thụ xong là hết.
///  • **Kafka** = luồng sự kiện để **stream/replay**: audit trail, analytics, feed sang data lake.
///    Giữ theo retention nên service mới join vẫn đọc lại được lịch sử (không thể làm với Service Bus).
///
/// Đây đúng cách hệ thống thật dùng song song hai broker, không phải "chọn 1 trong 2".
/// </summary>
public interface IEventStreamPublisher
{
    Task PublishAsync(string eventType, string jsonPayload, string key, CancellationToken ct = default);
}

public sealed class NoOpEventStreamPublisher : IEventStreamPublisher
{
    public Task PublishAsync(string eventType, string jsonPayload, string key, CancellationToken ct = default)
        => Task.CompletedTask;
}

public static class EventStreamExtensions
{
    /// <summary>Đăng ký stream Kafka; tắt (no-op) nếu không cấu hình để local vẫn chạy được.</summary>
    public static IServiceCollection AddKafkaEventStream(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<KafkaOptions>(config.GetSection("Kafka"));
        var enabled = !string.IsNullOrWhiteSpace(config["Kafka:BootstrapServers"]);
        if (enabled) services.AddSingleton<IEventStreamPublisher, KafkaEventStreamPublisher>();
        else services.AddSingleton<IEventStreamPublisher, NoOpEventStreamPublisher>();
        return services;
    }
}

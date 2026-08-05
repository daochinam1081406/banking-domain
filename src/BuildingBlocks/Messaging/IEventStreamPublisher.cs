using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Kênh event streaming (Kafka), tách khỏi <see cref="IEventBus"/> (Service Bus) có chủ đích:
/// Service Bus lo saga/command và tiêu thụ xong là hết; Kafka giữ theo retention nên audit,
/// analytics và service mới join vẫn đọc lại được lịch sử.
/// </summary>
public interface IEventStreamPublisher
{
    Task PublishAsync(string eventType, string jsonPayload, string key, CancellationToken ct = default);

    /// <summary>Mặc định lặp tuần tự; impl Kafka override để xếp hàng rồi Flush một lần.</summary>
    async Task PublishBatchAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default)
    {
        foreach (var m in messages)
            await PublishAsync(m.EventType, m.JsonPayload, m.MessageId, ct);
    }
}

public sealed class NoOpEventStreamPublisher : IEventStreamPublisher
{
    public Task PublishAsync(string eventType, string jsonPayload, string key, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task PublishBatchAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default)
        => Task.CompletedTask;
}

public static class EventStreamExtensions
{
    /// <summary>No-op nếu chưa cấu hình Kafka, để chạy local không cần broker.</summary>
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

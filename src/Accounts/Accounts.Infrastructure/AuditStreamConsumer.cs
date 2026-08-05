using Accounts.Domain;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Accounts.Infrastructure;

/// <summary>
/// Consumer Kafka dựng audit trail từ event stream. Consumer group riêng (`audit-service`) nên
/// đọc độc lập với các consumer khác; scale bằng cách chạy thêm instance — Kafka tự chia partition.
/// </summary>
public sealed class AuditStreamConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<AuditStreamConsumer> logger) : KafkaConsumerBase(options, logger)
{
    protected override string ConsumerGroup => options.Value.ConsumerGroup;

    protected override async Task HandleAsync(
        string eventType, string payload, string? correlationId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();

        db.AuditEvents.Add(AuditEvent.From(eventType, payload, correlationId));
        await db.SaveChangesAsync(ct);
    }
}

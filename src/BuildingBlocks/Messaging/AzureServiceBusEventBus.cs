using BuildingBlocks.Contracts;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BuildingBlocks.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging;

public sealed class AzureServiceBusEventBus : IEventBus, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly ILogger<AzureServiceBusEventBus> _logger;

    public AzureServiceBusEventBus(
        IOptions<ServiceBusOptions> options,
        ILogger<AzureServiceBusEventBus> logger)
    {
        var opt = options.Value;
        _client = new ServiceBusClient(opt.ConnectionString);
        _sender = _client.CreateSender(opt.TopicName);
        _logger = logger;
    }

    public Task PublishAsync<T>(T @event, CancellationToken ct = default)
        where T : IntegrationEvent
        => PublishRawAsync(
            @event.EventType,
            JsonSerializer.Serialize(@event, @event.GetType()),
            @event.EventId.ToString(),
            @event.SchemaVersion,
            ct);

    public async Task PublishRawAsync(
        string eventType, string jsonPayload, string messageId,
        int schemaVersion = 1, CancellationToken ct = default)
    {
        var correlationId = CorrelationContext.GetOrCreate();
        var message = new ServiceBusMessage(jsonPayload)
        {
            Subject       = eventType,      // subscription filter routing
            MessageId     = messageId,      // dedup / idempotency
            CorrelationId = correlationId,  // trace xuyên service
            ContentType   = "application/json",
        };
        message.ApplicationProperties[CorrelationContext.MessagePropertyName] = correlationId;
        message.ApplicationProperties[SchemaCompatibility.MessagePropertyName] = schemaVersion;

        await _sender.SendMessageAsync(message, ct);
        _logger.LogInformation("Published {EventType} {MessageId} → Service Bus (corr {CorrelationId})",
            eventType, messageId, correlationId);
    }

    /// <summary>
    /// Gộp nhiều message vào `ServiceBusMessageBatch` → 1 lần đi-về thay vì N.
    /// `TryAddMessage` trả false khi batch chạm trần kích thước của broker ⇒ gửi batch hiện tại
    /// rồi mở batch mới. Một message đơn lẻ quá lớn sẽ không bao giờ vào được batch nào — phải
    /// phát hiện và ném lỗi, nếu không vòng lặp sẽ quay vô hạn.
    /// </summary>
    public async Task PublishBatchAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) return;

        var batch = await _sender.CreateMessageBatchAsync(ct);
        var sent = 0;

        foreach (var m in messages)
        {
            var sbMessage = Build(m);
            if (batch.TryAddMessage(sbMessage)) continue;

            if (batch.Count == 0)
                throw new InvalidOperationException(
                    $"Message {m.MessageId} ({m.EventType}) vượt kích thước tối đa của Service Bus batch.");

            await _sender.SendMessagesAsync(batch, ct);
            sent += batch.Count;
            batch.Dispose();

            batch = await _sender.CreateMessageBatchAsync(ct);
            if (!batch.TryAddMessage(sbMessage))
                throw new InvalidOperationException(
                    $"Message {m.MessageId} ({m.EventType}) vượt kích thước tối đa của Service Bus batch.");
        }

        if (batch.Count > 0)
        {
            await _sender.SendMessagesAsync(batch, ct);
            sent += batch.Count;
        }
        batch.Dispose();

        _logger.LogInformation("Published {Count} event(s) → Service Bus theo lô", sent);
    }

    private static ServiceBusMessage Build(OutboxMessage m)
    {
        var correlationId = m.CorrelationId ?? Guid.NewGuid().ToString("N");
        var message = new ServiceBusMessage(m.JsonPayload)
        {
            Subject       = m.EventType,
            MessageId     = m.MessageId,
            CorrelationId = correlationId,
            ContentType   = "application/json",
        };
        message.ApplicationProperties[CorrelationContext.MessagePropertyName] = correlationId;
        message.ApplicationProperties[SchemaCompatibility.MessagePropertyName] = m.SchemaVersion;
        return message;
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }
}

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
            ct);

    public async Task PublishRawAsync(
        string eventType, string jsonPayload, string messageId, CancellationToken ct = default)
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

        await _sender.SendMessageAsync(message, ct);
        _logger.LogInformation("Published {EventType} {MessageId} → Service Bus (corr {CorrelationId})",
            eventType, messageId, correlationId);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }
}

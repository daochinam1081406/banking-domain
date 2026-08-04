using System.Text.Json;
using Azure.Messaging.ServiceBus;
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
        var message = new ServiceBusMessage(jsonPayload)
        {
            Subject     = eventType,   // subscription filter routing
            MessageId   = messageId,   // dedup / idempotency
            ContentType = "application/json",
        };
        await _sender.SendMessageAsync(message, ct);
        _logger.LogInformation("Published {EventType} {MessageId} → Service Bus topic", eventType, messageId);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }
}

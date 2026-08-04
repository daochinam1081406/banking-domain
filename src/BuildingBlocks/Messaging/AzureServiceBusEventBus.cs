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

    public async Task PublishAsync<T>(T @event, CancellationToken ct = default)
        where T : IntegrationEvent
    {
        var body = JsonSerializer.Serialize(@event, @event.GetType());
        var message = new ServiceBusMessage(body)
        {
            Subject     = @event.EventType,          // subscription filter routing
            MessageId   = @event.EventId.ToString(), // dedup / idempotency
            ContentType = "application/json",
        };
        await _sender.SendMessageAsync(message, ct);
        _logger.LogInformation(
            "Published {EventType} {EventId} → Service Bus topic", @event.EventType, @event.EventId);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }
}

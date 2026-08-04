using System.Text.Json;
using Azure;
using Azure.Messaging.EventGrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging;

public sealed class EventGridOptions
{
    public string TopicEndpoint { get; set; } = "";
    public string AccessKey { get; set; } = "";
}

/// <summary>
/// IEventBus impl trên Azure Event Grid — dùng cho event-notification/reactive (khác Service Bus:
/// Event Grid push, không giữ message, không ordering). Cùng abstraction, khác broker.
/// </summary>
public sealed class EventGridEventBus : IEventBus
{
    private readonly EventGridPublisherClient _client;
    private readonly ILogger<EventGridEventBus> _logger;

    public EventGridEventBus(IOptions<EventGridOptions> options, ILogger<EventGridEventBus> logger)
    {
        var opt = options.Value;
        _client = new EventGridPublisherClient(new Uri(opt.TopicEndpoint), new AzureKeyCredential(opt.AccessKey));
        _logger = logger;
    }

    public Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : IntegrationEvent
        => PublishRawAsync(@event.EventType,
            JsonSerializer.Serialize(@event, @event.GetType()), @event.EventId.ToString(), ct);

    public async Task PublishRawAsync(string eventType, string jsonPayload, string messageId, CancellationToken ct = default)
    {
        var egEvent = new EventGridEvent(
            subject: $"banking/{eventType}",
            eventType: eventType,
            dataVersion: "1.0",
            data: BinaryData.FromString(jsonPayload))
        {
            Id = messageId,
        };
        await _client.SendEventAsync(egEvent, ct);
        _logger.LogInformation("Published {EventType} {MessageId} → Event Grid", eventType, messageId);
    }
}

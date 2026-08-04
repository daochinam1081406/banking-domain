namespace BuildingBlocks.Messaging;

public sealed class ServiceBusOptions
{
    public string ConnectionString { get; set; } = "";
    public string TopicName { get; set; } = "banking-events";
    public string SubscriptionName { get; set; } = "default-subscription";
}

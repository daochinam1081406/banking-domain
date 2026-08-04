using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

public static class EventBusExtensions
{
    /// <summary>
    /// Chọn broker qua config "Messaging:Provider" — ServiceBus (mặc định) | EventGrid | Kafka.
    /// Cùng 1 IEventBus abstraction; đổi provider không đụng domain/application.
    /// </summary>
    public static IServiceCollection AddEventBus(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Messaging:Provider"] ?? "ServiceBus";
        switch (provider)
        {
            case "Kafka":
                services.Configure<KafkaOptions>(config.GetSection("Kafka"));
                services.AddSingleton<IEventBus, KafkaEventBus>();
                break;
            case "EventGrid":
                services.Configure<EventGridOptions>(config.GetSection("EventGrid"));
                services.AddSingleton<IEventBus, EventGridEventBus>();
                break;
            default:
                services.Configure<ServiceBusOptions>(config.GetSection("ServiceBus"));
                services.AddSingleton<IEventBus, AzureServiceBusEventBus>();
                break;
        }
        return services;
    }

    // Back-compat: ép dùng Azure Service Bus.
    public static IServiceCollection AddAzureServiceBus(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ServiceBusOptions>(config.GetSection("ServiceBus"));
        services.AddSingleton<IEventBus, AzureServiceBusEventBus>();
        return services;
    }
}

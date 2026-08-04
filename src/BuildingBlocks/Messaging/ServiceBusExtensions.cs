using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

public static class ServiceBusExtensions
{
    public static IServiceCollection AddAzureServiceBus(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ServiceBusOptions>(config.GetSection("ServiceBus"));
        services.AddSingleton<IEventBus, AzureServiceBusEventBus>();
        return services;
    }
}

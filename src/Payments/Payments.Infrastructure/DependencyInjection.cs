using BuildingBlocks.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Payments.Application;

namespace Payments.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentsInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        var connStr = config.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=payments;Username=postgres;Password=postgres";
        services.AddSingleton(NpgsqlDataSource.Create(connStr));

        services.AddScoped<ITransferRepository, DapperTransferRepository>();
        services.AddScoped<ITransferReadService, DapperTransferReadService>();
        services.AddScoped<InitiateTransferHandler>();

        services.AddAzureServiceBus(config);            // IEventBus (Azure Service Bus)
        services.AddHostedService<OutboxPublisher>();   // outbox → Service Bus
        return services;
    }
}

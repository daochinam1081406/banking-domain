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
        PersistenceConventions.Apply();   // Dapper type handlers (DateTimeOffset ↔ timestamptz)

        var connStr = config.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=payments;Username=postgres;Password=postgres";
        services.AddSingleton(NpgsqlDataSource.Create(connStr));

        services.AddScoped<ITransferRepository, DapperTransferRepository>();
        services.AddScoped<ITransferReadService, DapperTransferReadService>();
        services.AddScoped<InitiateTransferHandler>();

        // gRPC client → Accounts AccountCheck (sync validate số dư)
        var accountsGrpcUrl = config["Grpc:AccountsUrl"] ?? "http://localhost:8082";
        services.AddGrpcClient<Banking.Grpc.AccountCheck.AccountCheckClient>(o => o.Address = new Uri(accountsGrpcUrl))
            .AddStandardResilienceHandler();   // Polly: retry + circuit breaker + timeout cho transient fault
        services.AddScoped<IAccountChecker, GrpcAccountChecker>();

        services.AddScoped<ITransferStatusWriter, DapperTransferStatusWriter>();

        services.AddEventBus(config);                          // IEventBus: ServiceBus | EventGrid | Kafka
        services.AddHostedService<OutboxPublisher>();          // outbox → broker đã chọn
        services.AddHostedService<TransferResultConsumer>();   // saga: nhận kết quả từ Accounts

        // Health check thật — probe Postgres qua DI (không BuildServiceProvider giữa chừng).
        services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres");

        return services;
    }
}

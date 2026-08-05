using Accounts.Application;
using Accounts.Domain;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Accounts.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAccountsInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        var connStr = config.GetConnectionString("SqlServer")
            ?? "Server=localhost,1433;Database=accounts;User Id=sa;Password=Str0ng!Passw0rd;TrustServerCertificate=true;";
        services.AddDbContext<AccountsDbContext>(o => o.UseSqlServer(connStr));

        services.AddScoped<IAccountRepository, EfAccountRepository>();
        services.AddScoped<IInboxStore, EfInboxStore>();
        services.AddScoped<ILedgerRepository, EfLedgerRepository>();
        services.AddScoped<MoneyTransferApplier>();

        // Redis cache-aside cho read số dư — bật khi có ConnectionStrings:Redis, ngược lại no-op.
        var redisConn = config.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConn))
        {
            services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConn));
            services.AddScoped<EfAccountReadService>();
            services.AddScoped<IAccountReadService>(sp => new CachedAccountReadService(
                sp.GetRequiredService<EfAccountReadService>(),
                sp.GetRequiredService<IConnectionMultiplexer>()));
            services.AddSingleton<IAccountCacheInvalidator, RedisAccountCacheInvalidator>();
        }
        else
        {
            services.AddScoped<IAccountReadService, EfAccountReadService>();
            services.AddSingleton<IAccountCacheInvalidator, NoOpAccountCacheInvalidator>();
        }

        services.AddEventBus(config);                    // publish saga result events về Payments
        services.AddHostedService<MoneyTransferConsumer>();

        // Kafka: consumer group riêng dựng audit trail từ event stream
        services.Configure<KafkaOptions>(config.GetSection("Kafka"));
        if (!string.IsNullOrWhiteSpace(config["Kafka:BootstrapServers"]))
            services.AddHostedService<AuditStreamConsumer>();

        // Health check thật — probe DB (và Redis nếu bật), không phải trả "healthy" cứng.
        var health = services.AddHealthChecks().AddDbContextCheck<AccountsDbContext>("sqlserver");
        if (!string.IsNullOrWhiteSpace(redisConn))
            health.AddCheck("redis", new RedisHealthCheck(redisConn));

        return services;
    }
}

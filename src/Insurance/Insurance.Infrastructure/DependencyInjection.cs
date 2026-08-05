using BuildingBlocks.Messaging;
using BuildingBlocks.State;
using Insurance.Application;
using Insurance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Insurance.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInsuranceInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        var connStr = config.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=insurance;Username=postgres;Password=postgres";
        services.AddDbContext<InsuranceDbContext>(o => o.UseNpgsql(connStr));

        services.AddScoped<IPolicyRepository, EfPolicyRepository>();
        services.AddScoped<IClaimRepository, EfClaimRepository>();
        services.AddScoped<IInsuranceReadService, EfInsuranceReadService>();
        services.AddScoped<IInsuranceNumberGenerator, PostgresInsuranceNumberGenerator>();
        services.AddScoped<IClaimPayoutPublisher, ClaimPayoutPublisher>();
        services.AddScoped<InsuranceService>();

        services.AddEventBus(config);
        services.AddRedisState(config);   // distributed lock chống duyệt trùng
        services.AddHostedService<ClaimPayoutResultConsumer>();

        services.AddHealthChecks().AddDbContextCheck<InsuranceDbContext>("postgres");
        return services;
    }
}

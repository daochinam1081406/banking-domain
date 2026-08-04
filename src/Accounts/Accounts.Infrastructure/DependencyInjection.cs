using Accounts.Application;
using Accounts.Domain;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddScoped<IAccountReadService, EfAccountReadService>();
        services.AddScoped<MoneyTransferApplier>();

        services.Configure<ServiceBusOptions>(config.GetSection("ServiceBus"));
        services.AddHostedService<MoneyTransferConsumer>();
        return services;
    }
}

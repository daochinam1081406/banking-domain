using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Insurance.Infrastructure;

public static class DatabaseInitializer
{
    /// <summary>Migrate + tạo sequence sinh số. Retry vì container start ≠ DB ready.</summary>
    public static async Task MigrateInsuranceDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();

        for (var attempt = 1; ; attempt++)
        {
            try { await db.Database.MigrateAsync(); break; }
            catch when (attempt < 12) { await Task.Delay(TimeSpan.FromSeconds(3)); }
        }

        await db.Database.ExecuteSqlRawAsync("CREATE SEQUENCE IF NOT EXISTS policy_seq START 1");
        await db.Database.ExecuteSqlRawAsync("CREATE SEQUENCE IF NOT EXISTS claim_seq START 1");
    }
}

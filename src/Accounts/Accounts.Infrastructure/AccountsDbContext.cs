using Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Infrastructure;

public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(e =>
        {
            e.ToTable("accounts");
            e.HasKey(a => a.Id);
            e.Property(a => a.Number).HasMaxLength(50).IsRequired();
            e.HasIndex(a => a.Number).IsUnique();
            e.Property(a => a.Balance).HasPrecision(18, 2);
            e.Property(a => a.Currency).HasMaxLength(3).IsRequired();
            e.Property(a => a.UpdatedAt);
        });
    }
}

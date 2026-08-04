using Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Infrastructure;

public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

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
            e.Property(a => a.RowVersion).IsRowVersion();   // optimistic concurrency
        });

        modelBuilder.Entity<ProcessedMessage>(e =>
        {
            e.ToTable("processed_messages");
            e.HasKey(p => p.MessageId);                     // PK = dedup key
            e.Property(p => p.MessageId).HasMaxLength(100);
            e.Property(p => p.ProcessedAt);
        });
    }
}

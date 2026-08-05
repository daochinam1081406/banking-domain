using Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Infrastructure;

public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(e =>
        {
            e.ToTable("accounts");
            e.HasKey(a => a.Id);
            e.Property(a => a.Number).HasMaxLength(50).IsRequired();
            e.Property(a => a.OwnerId).HasMaxLength(100).IsRequired();
            e.HasIndex(a => a.OwnerId);
            e.HasIndex(a => a.Number).IsUnique();
            e.Property(a => a.Balance).HasPrecision(18, 2);
            e.Property(a => a.Currency).HasMaxLength(3).IsRequired();
            e.Property(a => a.UpdatedAt);
            e.Property(a => a.RowVersion).IsRowVersion();   // optimistic concurrency
        });

        modelBuilder.Entity<LedgerEntry>(e =>
        {
            e.ToTable("ledger_entries");
            e.HasKey(l => l.Id);
            e.Property(l => l.AccountNumber).HasMaxLength(50).IsRequired();
            e.Property(l => l.Currency).HasMaxLength(3).IsRequired();
            e.Property(l => l.Amount).HasPrecision(18, 2);
            e.Property(l => l.BalanceAfter).HasPrecision(18, 2);
            e.Property(l => l.Direction).HasConversion<string>().HasMaxLength(10);
            e.HasIndex(l => new { l.AccountNumber, l.CreatedAt });
            e.HasIndex(l => l.TransferId);
        });

        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_events");
            e.HasKey(a => a.Id);
            e.Property(a => a.EventType).HasMaxLength(100).IsRequired();
            e.Property(a => a.CorrelationId).HasMaxLength(64);
            e.Property(a => a.Payload).HasMaxLength(4000).IsRequired();
            e.HasIndex(a => a.ReceivedAt);
            e.HasIndex(a => a.CorrelationId);
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

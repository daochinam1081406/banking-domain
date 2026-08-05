using Insurance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Insurance.Infrastructure;

public sealed class InsuranceDbContext(DbContextOptions<InsuranceDbContext> options) : DbContext(options)
{
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Claim> Claims => Set<Claim>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Policy>(e =>
        {
            e.ToTable("policies");
            e.HasKey(p => p.Id);
            e.Property(p => p.PolicyNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(p => p.PolicyNumber).IsUnique();
            e.Property(p => p.PolicyHolderId).HasMaxLength(100).IsRequired();
            e.HasIndex(p => p.PolicyHolderId);
            e.Property(p => p.ProductCode).HasMaxLength(20).IsRequired();
            e.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            e.Property(p => p.PayoutAccount).HasMaxLength(50).IsRequired();
            e.Property(p => p.CoverageAmount).HasPrecision(18, 2);
            e.Property(p => p.PremiumAmount).HasPrecision(18, 2);
            e.Property(p => p.ClaimedAmount).HasPrecision(18, 2);
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Claim>(e =>
        {
            e.ToTable("claims");
            e.HasKey(c => c.Id);
            e.Property(c => c.ClaimNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(c => c.ClaimNumber).IsUnique();
            e.Property(c => c.PolicyNumber).HasMaxLength(30).IsRequired();
            e.Property(c => c.ClaimantId).HasMaxLength(100).IsRequired();
            e.HasIndex(c => c.ClaimantId);
            e.Property(c => c.Currency).HasMaxLength(3).IsRequired();
            e.Property(c => c.Description).HasMaxLength(1000).IsRequired();
            e.Property(c => c.RequestedAmount).HasPrecision(18, 2);
            e.Property(c => c.ApprovedAmount).HasPrecision(18, 2);
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.ReviewerId).HasMaxLength(100);
            e.Property(c => c.DecisionReason).HasMaxLength(500);

            // Hàng đợi giám định. Partial index chứ không phải composite (Status, CreatedAt):
            // trạng thái chờ chiếm ~40% bảng nên planner bỏ qua index thường. Index này chỉ chứa
            // dòng đang chờ và đã sẵn thứ tự CreatedAt DESC nên LIMIT đọc thẳng, không cần Sort.
            e.HasIndex(c => c.CreatedAt)
                .HasDatabaseName("IX_claims_PendingQueue")
                .HasFilter("\"Status\" IN ('Submitted','UnderReview')")
                .IsDescending(true);
            // Postgres không có kiểu `rowversion` như SQL Server — dùng system column `xmin`
            // làm concurrency token (shadow property, không cần cột thật trong bảng).
            e.Property<uint>("xmin")
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });
    }
}

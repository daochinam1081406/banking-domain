using Dapper;
using Npgsql;

namespace Payments.Infrastructure;

/// <summary>Tạo bảng transfers + outbox khi khởi động (Dapper không có migration như EF).</summary>
public static class SchemaInitializer
{
    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS transfers (
            id           UUID PRIMARY KEY,
            from_account VARCHAR(50)  NOT NULL,
            to_account   VARCHAR(50)  NOT NULL,
            amount       NUMERIC(18,2) NOT NULL,
            currency     VARCHAR(3)   NOT NULL,
            status       VARCHAR(20)  NOT NULL,
            created_at   TIMESTAMPTZ  NOT NULL
        );

        -- Kết quả saga từ Accounts (Completed/Failed)
        ALTER TABLE transfers ADD COLUMN IF NOT EXISTS completed_at    TIMESTAMPTZ;
        ALTER TABLE transfers ADD COLUMN IF NOT EXISTS failure_code    VARCHAR(50);
        ALTER TABLE transfers ADD COLUMN IF NOT EXISTS failure_reason  TEXT;

        CREATE TABLE IF NOT EXISTS outbox (
            id           UUID PRIMARY KEY,
            event_type   VARCHAR(100) NOT NULL,
            payload      JSONB        NOT NULL,
            status       VARCHAR(20)  NOT NULL DEFAULT 'PENDING',
            created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            published_at TIMESTAMPTZ
        );

        ALTER TABLE outbox ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(64);

        CREATE INDEX IF NOT EXISTS idx_outbox_pending
            ON outbox (created_at) WHERE status = 'PENDING';
        """;

    // Retry chờ Postgres sẵn sàng (container start ≠ DB ready). Portable — chạy cả trên k8s.
    public static async Task EnsureCreatedAsync(NpgsqlDataSource dataSource, CancellationToken ct = default)
    {
        const int maxAttempts = 12;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var conn = await dataSource.OpenConnectionAsync(ct);
                await conn.ExecuteAsync(new CommandDefinition(Ddl, cancellationToken: ct));
                return;
            }
            catch when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }
}

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

        CREATE TABLE IF NOT EXISTS outbox (
            id           UUID PRIMARY KEY,
            event_type   VARCHAR(100) NOT NULL,
            payload      JSONB        NOT NULL,
            status       VARCHAR(20)  NOT NULL DEFAULT 'PENDING',
            created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            published_at TIMESTAMPTZ
        );

        CREATE INDEX IF NOT EXISTS idx_outbox_pending
            ON outbox (created_at) WHERE status = 'PENDING';
        """;

    public static async Task EnsureCreatedAsync(NpgsqlDataSource dataSource, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(Ddl, cancellationToken: ct));
    }
}

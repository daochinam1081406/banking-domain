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
        ALTER TABLE outbox ADD COLUMN IF NOT EXISTS schema_version INT NOT NULL DEFAULT 1;

        -- Refresh token: chỉ lưu SHA-256 hash; family_id nhóm token cùng phiên (reuse detection)
        CREATE TABLE IF NOT EXISTS refresh_tokens (
            token_hash  VARCHAR(64)  PRIMARY KEY,
            subject     VARCHAR(100) NOT NULL,
            family_id   VARCHAR(64)  NOT NULL,
            expires_at  TIMESTAMPTZ  NOT NULL,
            revoked_at  TIMESTAMPTZ
        );
        CREATE INDEX IF NOT EXISTS idx_refresh_family ON refresh_tokens (family_id);

        -- Tài khoản đăng nhập: PBKDF2 hash + salt, có role (customer / adjuster)
        CREATE TABLE IF NOT EXISTS users (
            username      VARCHAR(50)  PRIMARY KEY,
            password_hash VARCHAR(200) NOT NULL,
            salt          VARCHAR(100) NOT NULL,
            role          VARCHAR(30)  NOT NULL,
            display_name  VARCHAR(100) NOT NULL,
            is_active     BOOLEAN      NOT NULL DEFAULT TRUE
        );

        -- Inbox: message nào đã xử lý rồi. PRIMARY KEY chính là cơ chế chống trùng —
        -- ghi cùng transaction với transfer nên không có khe hở giữa "đã chi tiền" và "đã đánh dấu".
        -- Thiếu bảng này thì broker giao lại message = chi tiền bồi thường lần nữa.
        CREATE TABLE IF NOT EXISTS processed_messages (
            message_id   VARCHAR(200) PRIMARY KEY,
            processed_at TIMESTAMPTZ  NOT NULL
        );

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

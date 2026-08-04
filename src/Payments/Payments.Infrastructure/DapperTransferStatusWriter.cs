using Dapper;
using Npgsql;
using Payments.Application;

namespace Payments.Infrastructure;

/// <summary>
/// Đóng vòng đời transfer theo event kết quả từ Accounts.
/// `WHERE status = 'Initiated'` → idempotent, message lặp không ghi đè trạng thái cuối.
/// </summary>
public sealed class DapperTransferStatusWriter(NpgsqlDataSource dataSource) : ITransferStatusWriter
{
    public async Task MarkCompletedAsync(Guid transferId, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE transfers
            SET status = 'Completed', completed_at = NOW()
            WHERE id = @Id AND status = 'Initiated'
            """, new { Id = transferId }, cancellationToken: ct));
    }

    public async Task MarkFailedAsync(Guid transferId, string errorCode, string reason, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE transfers
            SET status = 'Failed', failure_code = @Code, failure_reason = @Reason, completed_at = NOW()
            WHERE id = @Id AND status = 'Initiated'
            """, new { Id = transferId, Code = errorCode, Reason = reason }, cancellationToken: ct));
    }
}

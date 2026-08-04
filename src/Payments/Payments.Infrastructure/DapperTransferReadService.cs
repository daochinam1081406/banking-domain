using Dapper;
using Npgsql;
using Payments.Application;

namespace Payments.Infrastructure;

/// <summary>Read side — raw SQL tối ưu, alias cột → PascalCase khớp DTO.</summary>
public sealed class DapperTransferReadService(NpgsqlDataSource dataSource) : ITransferReadService
{
    public async Task<TransferDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<TransferDto>(new CommandDefinition(
            """
            SELECT id            AS Id,
                   from_account  AS FromAccount,
                   to_account    AS ToAccount,
                   amount        AS Amount,
                   currency      AS Currency,
                   status        AS Status,
                   created_at    AS CreatedAt
            FROM transfers
            WHERE id = @Id
            """,
            new { Id = id }, cancellationToken: ct));
    }
}

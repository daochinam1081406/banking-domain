using BuildingBlocks.Contracts;
using System.Text.Json;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using Dapper;
using Npgsql;
using Payments.Application;
using Payments.Domain;

namespace Payments.Infrastructure;

/// <summary>Write side — Dapper + Npgsql, persist transfer + outbox trong 1 transaction.</summary>
public sealed class DapperTransferRepository(NpgsqlDataSource dataSource) : ITransferRepository
{
    public async Task<bool> SaveWithOutboxAsync(
        Transfer transfer,
        IReadOnlyList<IntegrationEvent> integrationEvents,
        string? inboxMessageId = null,
        CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Dấu inbox đi TRƯỚC: trùng thì dừng ngay, chưa ghi gì cả.
        // `ON CONFLICT DO NOTHING` + đếm số dòng ⇒ để PRIMARY KEY của DB quyết định, không tự
        // kiểm tra "SELECT rồi INSERT" (hai consumer chạy song song sẽ lọt qua kiểu kiểm tra đó).
        if (inboxMessageId is not null)
        {
            var inserted = await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO processed_messages (message_id, processed_at)
                VALUES (@MessageId, NOW())
                ON CONFLICT (message_id) DO NOTHING
                """,
                new { MessageId = inboxMessageId }, tx, cancellationToken: ct));

            if (inserted == 0)
            {
                await tx.RollbackAsync(ct);
                return false;
            }
        }

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO transfers (id, from_account, to_account, amount, currency, status, created_at)
            VALUES (@Id, @FromAccount, @ToAccount, @Amount, @Currency, @Status, @CreatedAt)
            """,
            new
            {
                transfer.Id, transfer.FromAccount, transfer.ToAccount,
                transfer.Amount, transfer.Currency, transfer.Status, transfer.CreatedAt,
            }, tx, cancellationToken: ct));

        var correlationId = CorrelationContext.GetOrCreate();   // giữ lại để publisher (thread nền) khôi phục
        foreach (var e in integrationEvents)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO outbox (id, event_type, payload, status, created_at, correlation_id, schema_version)
                VALUES (@Id, @EventType, @Payload::jsonb, 'PENDING', NOW(), @CorrelationId, @SchemaVersion)
                """,
                new
                {
                    Id = e.EventId,
                    e.EventType,
                    Payload = JsonSerializer.Serialize(e, e.GetType()),
                    CorrelationId = correlationId,
                    e.SchemaVersion,
                }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return true;
    }
}

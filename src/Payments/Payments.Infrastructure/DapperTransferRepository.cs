using System.Text.Json;
using BuildingBlocks.Messaging;
using Dapper;
using Npgsql;
using Payments.Application;
using Payments.Domain;

namespace Payments.Infrastructure;

/// <summary>Write side — Dapper + Npgsql, persist transfer + outbox trong 1 transaction.</summary>
public sealed class DapperTransferRepository(NpgsqlDataSource dataSource) : ITransferRepository
{
    public async Task SaveWithOutboxAsync(
        Transfer transfer, IntegrationEvent integrationEvent, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

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

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO outbox (id, event_type, payload, status, created_at)
            VALUES (@Id, @EventType, @Payload::jsonb, 'PENDING', NOW())
            """,
            new
            {
                Id = integrationEvent.EventId,
                integrationEvent.EventType,
                Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
    }
}

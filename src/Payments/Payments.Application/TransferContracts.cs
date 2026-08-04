using BuildingBlocks.Messaging;
using Payments.Domain;

namespace Payments.Application;

public sealed record TransferDto(
    Guid Id, string FromAccount, string ToAccount, decimal Amount, string Currency,
    string Status, DateTimeOffset CreatedAt);

public sealed record InitiateTransferCommand(string FromAccount, string ToAccount, decimal Amount, string? Currency);

public sealed record InitiateTransferResult(Guid TransferId, string Status);

/// <summary>Write side — persist Transfer + outbox event trong 1 transaction (Outbox pattern).</summary>
public interface ITransferRepository
{
    Task SaveWithOutboxAsync(Transfer transfer, IntegrationEvent integrationEvent, CancellationToken ct = default);
}

/// <summary>Read side — Dapper query.</summary>
public interface ITransferReadService
{
    Task<TransferDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

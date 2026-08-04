using BuildingBlocks.Messaging;
using Payments.Domain;

namespace Payments.Application;

public sealed record TransferDto(
    Guid Id, string FromAccount, string ToAccount, decimal Amount, string Currency,
    string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, string? FailureCode, string? FailureReason);

public sealed record InitiateTransferCommand(
    string FromAccount, string ToAccount, decimal Amount, string? Currency)
{
    /// <summary>JWT subject của người gọi — dùng kiểm tra quyền sở hữu tài khoản nguồn.</summary>
    public string RequestedBy { get; init; } = string.Empty;
}

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

/// <summary>
/// Cập nhật trạng thái transfer khi nhận event kết quả từ Accounts (saga).
/// Idempotent: chỉ update khi đang ở trạng thái Initiated.
/// </summary>
public interface ITransferStatusWriter
{
    Task MarkCompletedAsync(Guid transferId, CancellationToken ct = default);
    Task MarkFailedAsync(Guid transferId, string errorCode, string reason, CancellationToken ct = default);
}

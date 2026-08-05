using BuildingBlocks.Contracts;
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
    /// <summary>
    /// Ghi transfer + TẤT CẢ outbox event + (tuỳ chọn) dấu inbox trong **một transaction**.
    ///
    /// <para><b>inboxMessageId</b> — bắt buộc truyền khi hàm này được gọi từ consumer. Broker chỉ
    /// bảo đảm at-least-once: cùng một message có thể được giao lại (Complete lỗi, lease hết hạn,
    /// service restart giữa chừng). Không có dấu inbox commit chung transaction thì mỗi lần giao lại
    /// sinh thêm một Transfer mới ⇒ <b>chi tiền nhiều lần cho cùng một hồ sơ bồi thường</b>.
    /// PRIMARY KEY của bảng inbox mới là thứ chặn, không phải logic trong bộ nhớ.</para>
    ///
    /// <para>Nhiều event: sự kiện báo kết quả saga phải đi CHUNG transaction với transfer. Nếu publish
    /// riêng ở ngoài, crash vào đúng khe giữa hai bước sẽ để hồ sơ kẹt ở Approved vĩnh viễn —
    /// tiền đã chuyển mà bên bảo hiểm không bao giờ biết.</para>
    /// </summary>
    /// <returns><c>false</c> nếu <paramref name="inboxMessageId"/> đã xử lý rồi (không ghi gì cả).</returns>
    Task<bool> SaveWithOutboxAsync(
        Transfer transfer,
        IReadOnlyList<IntegrationEvent> integrationEvents,
        string? inboxMessageId = null,
        CancellationToken ct = default);
}

public static class TransferRepositoryExtensions
{
    /// <summary>Trường hợp một event, gọi từ API (không qua broker nên không cần dấu inbox).</summary>
    public static Task SaveWithOutboxAsync(
        this ITransferRepository repo, Transfer transfer,
        IntegrationEvent integrationEvent, CancellationToken ct = default)
        => repo.SaveWithOutboxAsync(transfer, [integrationEvent], null, ct);
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

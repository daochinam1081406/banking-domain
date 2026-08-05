
namespace BuildingBlocks.Contracts;

/// <summary>Accounts phát khi đã áp số dư thành công — Payments consume để đóng transfer (Completed).</summary>
public sealed record TransferCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid TransferId { get; init; }
}

/// <summary>
/// Accounts phát khi không áp được (số dư không đủ, account không tồn tại…) — Payments consume để
/// đánh dấu Failed. Đây là compensating event của saga: lệnh chuyển tiền không âm thầm biến mất.
/// </summary>
public sealed record TransferFailedIntegrationEvent : IntegrationEvent
{
    public required Guid TransferId { get; init; }
    public required string Reason { get; init; }
    public required string ErrorCode { get; init; }
}


namespace BuildingBlocks.Contracts;

/// <summary>Payments phát khi 1 lệnh chuyển tiền được ghi nhận; Accounts consume để cập nhật số dư.</summary>
public sealed record MoneyTransferredIntegrationEvent : IntegrationEvent
{
    public required Guid TransferId { get; init; }
    public required string FromAccount { get; init; }
    public required string ToAccount { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

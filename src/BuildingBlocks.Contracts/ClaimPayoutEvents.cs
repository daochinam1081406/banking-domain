
namespace BuildingBlocks.Contracts;

/// <summary>Insurance duyệt bồi thường → Payments tạo lệnh chi trả cho khách.</summary>
public sealed record ClaimApprovedIntegrationEvent : IntegrationEvent
{
    public required Guid ClaimId { get; init; }
    public required string ClaimNumber { get; init; }
    public required string PolicyNumber { get; init; }
    public required string PayoutAccount { get; init; }   // tài khoản khách nhận tiền
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>Payments chi trả xong → Insurance đóng hồ sơ (Paid).</summary>
public sealed record ClaimPayoutCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid ClaimId { get; init; }
    public required Guid TransferId { get; init; }
}

/// <summary>Chi trả thất bại → Insurance ghi nhận để xử lý lại (compensating).</summary>
public sealed record ClaimPayoutFailedIntegrationEvent : IntegrationEvent
{
    public required Guid ClaimId { get; init; }
    public required string Reason { get; init; }
}

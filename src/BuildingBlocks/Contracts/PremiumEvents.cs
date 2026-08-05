using BuildingBlocks.Messaging;

namespace BuildingBlocks.Contracts;

/// <summary>
/// Bancassurance — thu phí bảo hiểm bằng cách trích nợ tài khoản ngân hàng của khách.
/// Insurance phát khi khách bấm đóng phí → Payments trích tiền KH → quỹ INS-FUND.
/// </summary>
public sealed record PremiumDueIntegrationEvent : IntegrationEvent
{
    public required Guid PolicyId { get; init; }
    public required string PolicyNumber { get; init; }
    public required string DebitAccount { get; init; }   // tài khoản khách bị trích nợ
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>Thu phí thành công → hợp đồng chuyển sang Active.</summary>
public sealed record PremiumCollectedIntegrationEvent : IntegrationEvent
{
    public required Guid PolicyId { get; init; }
    public required Guid TransferId { get; init; }
}

/// <summary>Thu phí thất bại (không đủ số dư…) → hợp đồng vẫn Draft, báo khách.</summary>
public sealed record PremiumCollectionFailedIntegrationEvent : IntegrationEvent
{
    public required Guid PolicyId { get; init; }
    public required string Reason { get; init; }
}

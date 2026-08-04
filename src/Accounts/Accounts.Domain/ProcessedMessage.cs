namespace Accounts.Domain;

/// <summary>
/// Inbox pattern — ghi lại message đã xử lý để đảm bảo idempotency.
/// Broker chỉ bảo đảm at-least-once; không có bảng này thì message lặp = trừ tiền 2 lần.
/// </summary>
public sealed class ProcessedMessage
{
    public string MessageId { get; private set; } = null!;
    public DateTimeOffset ProcessedAt { get; private set; }

    private ProcessedMessage() { } // EF Core

    public static ProcessedMessage Of(string messageId) => new()
    {
        MessageId = messageId,
        ProcessedAt = DateTimeOffset.UtcNow,
    };
}

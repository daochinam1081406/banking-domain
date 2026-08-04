namespace Accounts.Application;

/// <summary>
/// Inbox pattern — dedup message đã xử lý. `Mark` chỉ ghi vào change tracker; commit cùng
/// transaction với thay đổi số dư (SaveChanges) → không bao giờ lệch giữa "đã trừ tiền" và "đã đánh dấu".
/// </summary>
public interface IInboxStore
{
    Task<bool> AlreadyProcessedAsync(string messageId, CancellationToken ct = default);
    void MarkProcessed(string messageId);
}

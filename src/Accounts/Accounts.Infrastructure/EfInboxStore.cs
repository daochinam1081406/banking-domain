using Accounts.Application;
using Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Infrastructure;

public sealed class EfInboxStore(AccountsDbContext db) : IInboxStore
{
    public Task<bool> AlreadyProcessedAsync(string messageId, CancellationToken ct = default)
        => db.ProcessedMessages.AsNoTracking().AnyAsync(p => p.MessageId == messageId, ct);

    // Chỉ add vào change tracker — commit cùng SaveChanges với thay đổi số dư.
    // PK trên message_id chặn race 2 message trùng xử lý song song (unique violation → 1 cái abort).
    public void MarkProcessed(string messageId)
        => db.ProcessedMessages.Add(ProcessedMessage.Of(messageId));
}

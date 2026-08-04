using BuildingBlocks.Contracts;
using Payments.Domain;

namespace Payments.Application;

public sealed class InitiateTransferHandler(ITransferRepository repository)
{
    public async Task<InitiateTransferResult> HandleAsync(InitiateTransferCommand cmd, CancellationToken ct = default)
    {
        var transfer = Transfer.Initiate(cmd.FromAccount, cmd.ToAccount, cmd.Amount, cmd.Currency ?? "VND");

        var integrationEvent = new MoneyTransferredIntegrationEvent
        {
            TransferId  = transfer.Id,
            FromAccount = transfer.FromAccount,
            ToAccount   = transfer.ToAccount,
            Amount      = transfer.Amount,
            Currency    = transfer.Currency,
        };

        // Persist aggregate + outbox trong CÙNG transaction → OutboxPublisher sẽ đẩy lên Service Bus.
        await repository.SaveWithOutboxAsync(transfer, integrationEvent, ct);

        return new InitiateTransferResult(transfer.Id, transfer.Status);
    }
}

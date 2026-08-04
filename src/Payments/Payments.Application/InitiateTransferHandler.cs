using BuildingBlocks.Contracts;
using Payments.Domain;
using Payments.Domain.Exceptions;

namespace Payments.Application;

public sealed class InitiateTransferHandler(ITransferRepository repository, IAccountChecker accountChecker)
{
    public async Task<InitiateTransferResult> HandleAsync(InitiateTransferCommand cmd, CancellationToken ct = default)
    {
        var transfer = Transfer.Initiate(cmd.FromAccount, cmd.ToAccount, cmd.Amount, cmd.Currency ?? "VND");

        // Sync gRPC → Accounts: validate tài khoản nguồn tồn tại + đủ số dư trước khi ghi nhận.
        var check = await accountChecker.CheckAsync(transfer.FromAccount, ct);
        if (!check.Exists)
            throw new PaymentsDomainException($"Tài khoản nguồn {transfer.FromAccount} không tồn tại.", "FROM_NOT_FOUND");
        if (check.Balance < transfer.Amount)
            throw new PaymentsDomainException(
                $"Số dư không đủ: {check.Balance} < {transfer.Amount}.", "INSUFFICIENT_FUNDS");

        var integrationEvent = new MoneyTransferredIntegrationEvent
        {
            TransferId  = transfer.Id,
            FromAccount = transfer.FromAccount,
            ToAccount   = transfer.ToAccount,
            Amount      = transfer.Amount,
            Currency    = transfer.Currency,
        };

        // Persist aggregate + outbox trong CÙNG transaction → OutboxPublisher đẩy lên broker.
        await repository.SaveWithOutboxAsync(transfer, integrationEvent, ct);
        return new InitiateTransferResult(transfer.Id, transfer.Status);
    }
}

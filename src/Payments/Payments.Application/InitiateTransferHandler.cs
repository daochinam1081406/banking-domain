using BuildingBlocks.Contracts;
using Payments.Domain;
using Payments.Domain.Exceptions;

namespace Payments.Application;

public sealed class InitiateTransferHandler(ITransferRepository repository, IAccountChecker accountChecker)
{
    public async Task<InitiateTransferResult> HandleAsync(InitiateTransferCommand cmd, CancellationToken ct = default)
    {
        var transfer = Transfer.Initiate(cmd.FromAccount, cmd.ToAccount, cmd.Amount, cmd.Currency ?? "VND");

        // Sync gRPC → Accounts: validate tồn tại + quyền sở hữu + tiền tệ + số dư trước khi ghi nhận.
        var from = await accountChecker.CheckAsync(transfer.FromAccount, ct);
        if (!from.Exists)
            throw new PaymentsDomainException($"Tài khoản nguồn {transfer.FromAccount} không tồn tại.", "FROM_NOT_FOUND");
        if (!string.Equals(from.OwnerId, cmd.RequestedBy, StringComparison.Ordinal))
            throw new PaymentsDomainException(
                "Không có quyền chuyển tiền từ tài khoản này.", "FORBIDDEN_ACCOUNT");
        if (!string.Equals(from.Currency, transfer.Currency, StringComparison.Ordinal))
            throw new PaymentsDomainException(
                $"Tài khoản nguồn dùng {from.Currency}, lệnh {transfer.Currency}.", "CURRENCY_MISMATCH");
        if (from.Balance < transfer.Amount)
            throw new PaymentsDomainException(
                $"Số dư không đủ: {from.Balance} < {transfer.Amount}.", "INSUFFICIENT_FUNDS");

        var to = await accountChecker.CheckAsync(transfer.ToAccount, ct);
        if (!to.Exists)
            throw new PaymentsDomainException($"Tài khoản đích {transfer.ToAccount} không tồn tại.", "TO_NOT_FOUND");
        if (!string.Equals(to.Currency, transfer.Currency, StringComparison.Ordinal))
            throw new PaymentsDomainException(
                $"Tài khoản đích dùng {to.Currency}, lệnh {transfer.Currency}.", "CURRENCY_MISMATCH");

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

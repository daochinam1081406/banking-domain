using Banking.Grpc;
using Payments.Application;

namespace Payments.Infrastructure;

/// <summary>IAccountChecker impl — gọi gRPC AccountCheck service của Accounts.</summary>
public sealed class GrpcAccountChecker(AccountCheck.AccountCheckClient client) : IAccountChecker
{
    public async Task<AccountCheckResult> CheckAsync(string number, CancellationToken ct = default)
    {
        var reply = await client.CheckAsync(new AccountCheckRequest { Number = number }, cancellationToken: ct);
        return new AccountCheckResult(reply.Exists, (decimal)reply.Balance, reply.Currency);
    }
}

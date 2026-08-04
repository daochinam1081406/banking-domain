using Accounts.Application;
using Banking.Grpc;
using Grpc.Core;

namespace Accounts.Api;

/// <summary>gRPC endpoint — Payments gọi sync để validate tài khoản/số dư trước khi tạo transfer.</summary>
public sealed class AccountCheckService(IAccountReadService reads) : AccountCheck.AccountCheckBase
{
    public override async Task<AccountCheckReply> Check(AccountCheckRequest request, ServerCallContext context)
    {
        var dto = await reads.GetByNumberAsync(request.Number, context.CancellationToken);
        return new AccountCheckReply
        {
            Exists = dto is not null,
            Balance = dto is null ? 0 : (double)dto.Balance,
            Currency = dto?.Currency ?? string.Empty,
        };
    }
}

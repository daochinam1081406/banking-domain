using System.Security.Claims;
using Accounts.Application;
using Accounts.Domain;
using Accounts.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

/// <summary>Tài khoản + sao kê. Mọi truy cập giới hạn theo chủ sở hữu (JWT subject).</summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/accounts").WithTags("Accounts").RequireAuthorization();

        g.MapGet("/", List).WithName("ListAccounts");
        g.MapGet("/{number}", GetByNumber).WithName("GetAccount");
        g.MapGet("/{number}/statement", GetStatement).WithName("GetStatement");
        g.MapPost("/", Open).WithName("OpenAccount");

        app.MapGet("/api/audit", ListAudit).WithTags("Audit").RequireAuthorization().WithName("ListAudit");
        return app;
    }

    private static async Task<IResult> List(
        ClaimsPrincipal user, IAccountReadService reads, CancellationToken ct)
        => Results.Ok(await reads.ListAsync(user.Subject(), ct));

    private static async Task<IResult> GetByNumber(
        string number, ClaimsPrincipal user, IAccountReadService reads, CancellationToken ct)
    {
        var dto = await reads.GetByNumberAsync(number, ct);
        if (dto is null) return Results.NotFound();
        // 404 thay vì 403 — không tiết lộ tài khoản người khác có tồn tại.
        return dto.OwnerId == user.Subject() ? Results.Ok(dto) : Results.NotFound();
    }

    /// <summary>Sao kê = bút toán kép bất biến, nguồn sự thật để đối soát.</summary>
    private static async Task<IResult> GetStatement(
        string number, ClaimsPrincipal user, IAccountReadService reads,
        ILedgerRepository ledger, CancellationToken ct)
    {
        var dto = await reads.GetByNumberAsync(number, ct);
        if (dto is null || dto.OwnerId != user.Subject()) return Results.NotFound();
        return Results.Ok(await ledger.GetStatementAsync(number, 50, ct));
    }

    private static async Task<IResult> Open(
        OpenAccountRequest req, ClaimsPrincipal user, IAccountRepository repo, CancellationToken ct)
    {
        // Chủ sở hữu lấy từ JWT, không cho client tự khai.
        var account = Account.Open(req.Number, user.Subject(), req.InitialBalance, req.Currency ?? "VND");
        await repo.AddAsync(account, ct);
        await repo.SaveChangesAsync(ct);
        return Results.Created($"/api/accounts/{account.Number}",
            new { account.Number, account.Balance, account.Currency });
    }

    /// <summary>Audit trail dựng từ Kafka event stream.</summary>
    private static async Task<IResult> ListAudit(AccountsDbContext db, CancellationToken ct)
        => Results.Ok(await db.AuditEvents.AsNoTracking()
            .OrderByDescending(a => a.ReceivedAt).Take(30)
            .Select(a => new { a.EventType, a.CorrelationId, a.ReceivedAt })
            .ToListAsync(ct));
}

public sealed record OpenAccountRequest(string Number, decimal InitialBalance, string? Currency);

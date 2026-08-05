using System.Security.Claims;
using BuildingBlocks.Auth;
using BuildingBlocks.State;
using Insurance.Application;

namespace Insurance.Api.Endpoints;

/// <summary>
/// Yêu cầu bồi thường. Duyệt/từ chối yêu cầu role <c>adjuster</c> — khách hàng KHÔNG được
/// tự duyệt hồ sơ của mình (segregation of duties).
/// </summary>
public static class ClaimEndpoints
{
    public static IEndpointRouteBuilder MapClaimEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/claims")
            .WithTags("Claims")
            .RequireAuthorization();

        g.MapGet("/", List).WithName("ListClaims");
        g.MapGet("/{id:guid}", GetById).WithName("GetClaim");
        g.MapPost("/", Submit).WithName("SubmitClaim");

        // Chỉ giám định viên
        g.MapPost("/{id:guid}/approve", Approve).WithName("ApproveClaim").RequireAuthorization("adjuster-only");
        g.MapPost("/{id:guid}/reject", Reject).WithName("RejectClaim").RequireAuthorization("adjuster-only");

        return app;
    }

    /// <summary>Khách chỉ thấy hồ sơ của mình; giám định viên thấy hàng chờ cần xử lý.</summary>
    private static async Task<IResult> List(
        ClaimsPrincipal user, IInsuranceReadService reads, CancellationToken ct, int? limit = null)
    {
        var take = QueryLimits.Clamp(limit);
        return Results.Ok(user.IsInRole(Roles.Adjuster)
            ? await reads.ListPendingClaimsAsync(take, ct)
            : await reads.ListClaimsAsync(user.Subject(), take, ct));
    }

    private static async Task<IResult> GetById(
        Guid id, ClaimsPrincipal user, IInsuranceReadService reads, CancellationToken ct)
    {
        var dto = await reads.GetClaimAsync(id, ct);
        if (dto is null) return Results.NotFound();
        return dto.ClaimantId == user.Subject() || user.IsInRole(Roles.Adjuster)
            ? Results.Ok(dto) : Results.NotFound();
    }

    private static async Task<IResult> Submit(
        SubmitClaimCommand cmd, ClaimsPrincipal user, InsuranceService svc, CancellationToken ct)
    {
        var r = await svc.SubmitClaimAsync(cmd with { ClaimantId = user.Subject() }, ct);
        return Results.Created($"/api/claims/{r.ClaimId}", r);
    }

    /// <summary>Duyệt → phát ClaimApproved → Payments chi trả (saga xuyên service).</summary>
    private static async Task<IResult> Approve(
        Guid id, ApproveClaimRequest req, ClaimsPrincipal user,
        InsuranceService svc, IDistributedLock locks, CancellationToken ct)
    {
        // Khoá theo claim: 2 giám định viên bấm cùng lúc → chỉ 1 người qua.
        await using var lease = await locks.AcquireAsync($"claim:{id}", TimeSpan.FromSeconds(30), ct);
        if (lease is null)
            return Results.Conflict(new { errorCode = "CLAIM_LOCKED", detail = "Hồ sơ đang được xử lý bởi người khác." });

        await svc.ApproveClaimAsync(new ApproveClaimCommand(id, req.AssessedCost) { ReviewerId = user.Subject() }, ct);
        return Results.Accepted($"/api/claims/{id}", new { claimId = id, status = "Approved", payout = "processing" });
    }

    private static async Task<IResult> Reject(
        Guid id, RejectClaimRequest req, ClaimsPrincipal user, InsuranceService svc, CancellationToken ct)
    {
        await svc.RejectClaimAsync(new RejectClaimCommand(id, req.Reason) { ReviewerId = user.Subject() }, ct);
        return Results.NoContent();
    }
}

/// <summary>Chi phí giám định công nhận — số BH thực trả do hợp đồng quyết định.</summary>
public sealed record ApproveClaimRequest(decimal AssessedCost);
public sealed record RejectClaimRequest(string Reason);

using System.Security.Claims;
using Insurance.Application;

namespace Insurance.Api.Endpoints;

/// <summary>Hợp đồng bảo hiểm — phát hành, xem, đóng phí (bancassurance).</summary>
public static class PolicyEndpoints
{
    public static IEndpointRouteBuilder MapPolicyEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/policies")
            .WithTags("Policies")
            .RequireAuthorization();

        g.MapGet("/", List).WithName("ListPolicies");
        g.MapGet("/{policyNumber}", GetByNumber).WithName("GetPolicy");
        g.MapPost("/", Issue).WithName("IssuePolicy");
        g.MapPost("/{policyNumber}/pay-premium", PayPremium).WithName("PayPremium");

        return app;
    }

    private static async Task<IResult> List(
        ClaimsPrincipal user, IInsuranceReadService reads, CancellationToken ct)
        => Results.Ok(await reads.ListPoliciesAsync(user.Subject(), ct));

    private static async Task<IResult> GetByNumber(
        string policyNumber, ClaimsPrincipal user, IInsuranceReadService reads, CancellationToken ct)
    {
        var dto = await reads.GetPolicyAsync(policyNumber, ct);
        // Trả 404 (không phải 403) khi không phải chủ hợp đồng — không tiết lộ hợp đồng có tồn tại.
        return dto is null || dto.PolicyHolderId != user.Subject() ? Results.NotFound() : Results.Ok(dto);
    }

    private static async Task<IResult> Issue(
        IssuePolicyCommand cmd, ClaimsPrincipal user, InsuranceService svc, CancellationToken ct)
    {
        var r = await svc.IssuePolicyAsync(cmd with { PolicyHolderId = user.Subject() }, ct);
        return Results.Created($"/api/policies/{r.PolicyNumber}", r);
    }

    /// <summary>Trích nợ phí từ tài khoản ngân hàng; hợp đồng Active khi thu được tiền (saga).</summary>
    private static async Task<IResult> PayPremium(
        string policyNumber, PayPremiumRequest? req, ClaimsPrincipal user,
        InsuranceService svc, CancellationToken ct)
    {
        await svc.RequestPremiumCollectionAsync(policyNumber, user.Subject(), req?.DebitAccount ?? "", ct);
        return Results.Accepted($"/api/policies/{policyNumber}",
            new { policyNumber, status = "PremiumCollecting" });
    }
}

public sealed record PayPremiumRequest(string? DebitAccount);

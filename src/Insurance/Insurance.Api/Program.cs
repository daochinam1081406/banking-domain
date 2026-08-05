using System.Security.Claims;
using System.Text;
using BuildingBlocks.Auth;
using BuildingBlocks.Http;
using BuildingBlocks.Observability;
using BuildingBlocks.State;
using Insurance.Application;
using Insurance.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("insurance-api");
builder.Services.AddInsuranceInfrastructure(builder.Configuration);

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddSingleton(jwt);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
    });
// Tách quyền: chỉ giám định viên mới được duyệt/từ chối hồ sơ (segregation of duties —
// khách hàng KHÔNG được tự duyệt hồ sơ của chính mình).
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("adjuster-only", p => p.RequireRole(Roles.Adjuster));
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();
app.UseObservability();
app.UseCors();
app.UseDomainExceptionHandler();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
    for (var attempt = 1; ; attempt++)
    {
        try { db.Database.Migrate(); break; }
        catch when (attempt < 12) { await Task.Delay(TimeSpan.FromSeconds(3)); }
    }
    // Sequence sinh số hợp đồng / hồ sơ bồi thường
    await db.Database.ExecuteSqlRawAsync("CREATE SEQUENCE IF NOT EXISTS policy_seq START 1");
    await db.Database.ExecuteSqlRawAsync("CREATE SEQUENCE IF NOT EXISTS claim_seq START 1");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Insurance.Api — /api/policies · /api/claims");
app.MapHealthChecks("/health");

// ── Hợp đồng bảo hiểm ────────────────────────────────────────────
app.MapGet("/api/policies", async (ClaimsPrincipal user, IInsuranceReadService reads, CancellationToken ct) =>
    Results.Ok(await reads.ListPoliciesAsync(user.Subject(), ct))).RequireAuthorization();

app.MapGet("/api/policies/{policyNumber}", async (
    string policyNumber, ClaimsPrincipal user, IInsuranceReadService reads, CancellationToken ct) =>
{
    var dto = await reads.GetPolicyAsync(policyNumber, ct);
    return dto is null || dto.PolicyHolderId != user.Subject() ? Results.NotFound() : Results.Ok(dto);
}).RequireAuthorization();

app.MapPost("/api/policies", async (
    IssuePolicyCommand cmd, ClaimsPrincipal user, InsuranceService svc, CancellationToken ct) =>
{
    var r = await svc.IssuePolicyAsync(cmd with { PolicyHolderId = user.Subject() }, ct);
    return Results.Created($"/api/policies/{r.PolicyNumber}", r);
}).RequireAuthorization();

// Đóng phí (bancassurance): trích nợ tài khoản ngân hàng → hợp đồng Active khi thu được tiền
app.MapPost("/api/policies/{policyNumber}/pay-premium", async (
    string policyNumber, PayPremiumRequest? req, ClaimsPrincipal user,
    InsuranceService svc, CancellationToken ct) =>
{
    await svc.RequestPremiumCollectionAsync(policyNumber, user.Subject(), req?.DebitAccount ?? "", ct);
    return Results.Accepted($"/api/policies/{policyNumber}",
        new { policyNumber, status = "PremiumCollecting" });
}).RequireAuthorization();

// ── Yêu cầu bồi thường ───────────────────────────────────────────
// Khách chỉ thấy hồ sơ của mình; giám định viên thấy toàn bộ hàng chờ xử lý.
app.MapGet("/api/claims", async (ClaimsPrincipal user, IInsuranceReadService reads, CancellationToken ct) =>
    Results.Ok(user.IsInRole(Roles.Adjuster)
        ? await reads.ListAllClaimsAsync(ct)
        : await reads.ListClaimsAsync(user.Subject(), ct))).RequireAuthorization();

app.MapGet("/api/claims/{id:guid}", async (
    Guid id, ClaimsPrincipal user, IInsuranceReadService reads, CancellationToken ct) =>
{
    var dto = await reads.GetClaimAsync(id, ct);
    if (dto is null) return Results.NotFound();
    return dto.ClaimantId == user.Subject() || user.IsInRole(Roles.Adjuster)
        ? Results.Ok(dto) : Results.NotFound();
}).RequireAuthorization();

app.MapPost("/api/claims", async (
    SubmitClaimCommand cmd, ClaimsPrincipal user, InsuranceService svc, CancellationToken ct) =>
{
    var r = await svc.SubmitClaimAsync(cmd with { ClaimantId = user.Subject() }, ct);
    return Results.Created($"/api/claims/{r.ClaimId}", r);
}).RequireAuthorization();

// Giám định viên duyệt → phát ClaimApproved → Payments chi trả (saga xuyên service)
app.MapPost("/api/claims/{id:guid}/approve", async (
    Guid id, ApproveClaimRequest req, ClaimsPrincipal user, InsuranceService svc,
    IDistributedLock locks, CancellationToken ct) =>
{
    // Khoá theo claim: 2 giám định viên bấm duyệt cùng lúc → chỉ 1 người qua, người kia nhận 409.
    await using var lease = await locks.AcquireAsync($"claim:{id}", TimeSpan.FromSeconds(30), ct);
    if (lease is null)
        return Results.Conflict(new { errorCode = "CLAIM_LOCKED", detail = "Hồ sơ đang được xử lý bởi người khác." });

    await svc.ApproveClaimAsync(new ApproveClaimCommand(id, req.AssessedCost) { ReviewerId = user.Subject() }, ct);
    return Results.Accepted($"/api/claims/{id}", new { claimId = id, status = "Approved", payout = "processing" });
}).RequireAuthorization("adjuster-only");

app.MapPost("/api/claims/{id:guid}/reject", async (
    Guid id, RejectClaimRequest req, ClaimsPrincipal user, InsuranceService svc, CancellationToken ct) =>
{
    await svc.RejectClaimAsync(new RejectClaimCommand(id, req.Reason) { ReviewerId = user.Subject() }, ct);
    return Results.NoContent();
}).RequireAuthorization("adjuster-only");

app.Run();

/// <summary>Chi phí giám định công nhận — số BH thực trả do hợp đồng quyết định.</summary>
public sealed record ApproveClaimRequest(decimal AssessedCost);
public sealed record PayPremiumRequest(string? DebitAccount);
public sealed record RejectClaimRequest(string Reason);

internal static class ClaimsPrincipalExtensions
{
    public static string Subject(this ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? user.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException("JWT thiếu subject.");
}

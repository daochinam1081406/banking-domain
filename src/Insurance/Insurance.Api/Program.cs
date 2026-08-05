using System.Security.Claims;
using System.Text;
using BuildingBlocks.Auth;
using BuildingBlocks.Http;
using BuildingBlocks.Observability;
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
builder.Services.AddAuthorization();
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

// Đóng phí → hợp đồng có hiệu lực
app.MapPost("/api/policies/{policyNumber}/activate", async (
    string policyNumber, ClaimsPrincipal user, InsuranceService svc, CancellationToken ct) =>
{
    await svc.ActivatePolicyAsync(policyNumber, user.Subject(), ct);
    return Results.NoContent();
}).RequireAuthorization();

// ── Yêu cầu bồi thường ───────────────────────────────────────────
app.MapGet("/api/claims", async (ClaimsPrincipal user, IInsuranceReadService reads, CancellationToken ct) =>
    Results.Ok(await reads.ListClaimsAsync(user.Subject(), ct))).RequireAuthorization();

app.MapGet("/api/claims/{id:guid}", async (
    Guid id, ClaimsPrincipal user, IInsuranceReadService reads, CancellationToken ct) =>
{
    var dto = await reads.GetClaimAsync(id, ct);
    return dto is null || dto.ClaimantId != user.Subject() ? Results.NotFound() : Results.Ok(dto);
}).RequireAuthorization();

app.MapPost("/api/claims", async (
    SubmitClaimCommand cmd, ClaimsPrincipal user, InsuranceService svc, CancellationToken ct) =>
{
    var r = await svc.SubmitClaimAsync(cmd with { ClaimantId = user.Subject() }, ct);
    return Results.Created($"/api/claims/{r.ClaimId}", r);
}).RequireAuthorization();

// Giám định viên duyệt → phát ClaimApproved → Payments chi trả (saga xuyên service)
app.MapPost("/api/claims/{id:guid}/approve", async (
    Guid id, ApproveClaimRequest req, ClaimsPrincipal user, InsuranceService svc, CancellationToken ct) =>
{
    await svc.ApproveClaimAsync(new ApproveClaimCommand(id, req.ApprovedAmount) { ReviewerId = user.Subject() }, ct);
    return Results.Accepted($"/api/claims/{id}", new { claimId = id, status = "Approved", payout = "processing" });
}).RequireAuthorization();

app.MapPost("/api/claims/{id:guid}/reject", async (
    Guid id, RejectClaimRequest req, ClaimsPrincipal user, InsuranceService svc, CancellationToken ct) =>
{
    await svc.RejectClaimAsync(new RejectClaimCommand(id, req.Reason) { ReviewerId = user.Subject() }, ct);
    return Results.NoContent();
}).RequireAuthorization();

app.Run();

public sealed record ApproveClaimRequest(decimal ApprovedAmount);
public sealed record RejectClaimRequest(string Reason);

internal static class ClaimsPrincipalExtensions
{
    public static string Subject(this ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? user.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException("JWT thiếu subject.");
}

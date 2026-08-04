using System.Security.Claims;
using System.Text;
using BuildingBlocks.Auth;
using BuildingBlocks.Http;
using BuildingBlocks.Observability;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Payments.Application;
using Payments.Domain.Exceptions;
using Payments.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("payments-api");   // Serilog structured logs + OpenTelemetry tracing
builder.Services.AddPaymentsInfrastructure(builder.Configuration);

// ── JWT auth (OAuth2/OIDC bearer) ────────────────────────────────
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
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));   // FE React gọi cross-origin

// Application Insights — chỉ bật khi có connection string (không ảnh hưởng local/dev).
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();
app.UseObservability();   // correlation id + request logging
app.UseCors();
app.UseDomainExceptionHandler();   // domain error → 400 ProblemDetails

using (var scope = app.Services.CreateScope())
{
    var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
    await SchemaInitializer.EnsureCreatedAsync(dataSource);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Payments.Api — POST /api/transfers");
app.MapHealthChecks("/health");   // probe Postgres thật

// ── Auth: access token ngắn hạn + refresh token rotation (production: thay bằng IdP OAuth2/OIDC) ──
app.MapPost("/auth/login", async (TokenRequest req, TokenService tokens, CancellationToken ct) =>
    Results.Ok(await tokens.LoginAsync(req.Subject ?? "demo-user", req.Role ?? "customer", ct)));

app.MapPost("/auth/refresh", async (RefreshRequest req, TokenService tokens, CancellationToken ct) =>
    Results.Ok(await tokens.RefreshAsync(req.RefreshToken, ct)));

app.MapPost("/auth/logout", async (RefreshRequest req, TokenService tokens, CancellationToken ct) =>
{
    await tokens.LogoutAsync(req.RefreshToken, ct);
    return Results.NoContent();
});

// Alias cũ — giữ để README/script hiện có không gãy.
app.MapPost("/token", (TokenRequest req, JwtOptions opt) =>
    Results.Ok(new { token = JwtTokenFactory.Issue(opt, req.Subject ?? "demo-user", req.Role ?? "customer") }));

app.MapPost("/api/transfers", async (
    InitiateTransferCommand cmd, ClaimsPrincipal user,
    InitiateTransferHandler handler, CancellationToken ct) =>
{
    try
    {
        // Người gọi lấy từ JWT, không tin client tự khai.
        var subject = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value ?? "";
        var result = await handler.HandleAsync(cmd with { RequestedBy = subject }, ct);
        return Results.Accepted($"/api/transfers/{result.TransferId}", result);
    }
    catch (PaymentsDomainException ex)
    {
        return Results.BadRequest(new { error = ex.ErrorCode, message = ex.Message });
    }
}).RequireAuthorization();

app.MapGet("/api/transfers/{id:guid}", async (Guid id, ITransferReadService reads, CancellationToken ct) =>
{
    var dto = await reads.GetByIdAsync(id, ct);
    return dto is null ? Results.NotFound() : Results.Ok(dto);
}).RequireAuthorization();

app.Run();

public sealed record TokenRequest(string? Subject, string? Role);
public sealed record RefreshRequest(string RefreshToken);

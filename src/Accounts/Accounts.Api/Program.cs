using System.Text;
using Accounts.Api;
using Accounts.Application;
using Accounts.Domain;
using Accounts.Infrastructure;
using BuildingBlocks.Auth;
using BuildingBlocks.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAccountsInfrastructure(builder.Configuration);
builder.Services.AddGrpc();

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
app.UseCors();
app.UseDomainExceptionHandler();   // domain error → 400 ProblemDetails, không lộ stack trace

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
    // Retry chờ SQL Server sẵn sàng (container start ≠ DB ready).
    for (var attempt = 1; ; attempt++)
    {
        try { db.Database.Migrate(); break; }
        catch when (attempt < 12) { await Task.Delay(TimeSpan.FromSeconds(3)); }
    }
    AccountSeeder.Seed(db);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<AccountCheckService>();
app.MapGet("/", () => "Accounts.Api — GET /api/accounts/{number} · gRPC AccountCheck");
app.MapHealthChecks("/health");   // probe SQL Server (+ Redis) thật

app.MapPost("/token", (TokenRequest req, JwtOptions opt) =>
    Results.Ok(new { token = JwtTokenFactory.Issue(opt, req.Subject ?? "demo-user", req.Role ?? "customer") }));

app.MapGet("/api/accounts", async (IAccountReadService reads, CancellationToken ct) =>
    Results.Ok(await reads.ListAsync(ct))).RequireAuthorization();

app.MapGet("/api/accounts/{number}", async (string number, IAccountReadService reads, CancellationToken ct) =>
{
    var dto = await reads.GetByNumberAsync(number, ct);
    return dto is null ? Results.NotFound() : Results.Ok(dto);
}).RequireAuthorization();

app.MapPost("/api/accounts", async (OpenAccountRequest req, IAccountRepository repo, CancellationToken ct) =>
{
    var account = Account.Open(req.Number, req.InitialBalance, req.Currency ?? "VND");
    await repo.AddAsync(account, ct);
    await repo.SaveChangesAsync(ct);
    return Results.Created($"/api/accounts/{account.Number}",
        new { account.Number, account.Balance, account.Currency });
}).RequireAuthorization();

app.Run();

public sealed record OpenAccountRequest(string Number, decimal InitialBalance, string? Currency);
public sealed record TokenRequest(string? Subject, string? Role);

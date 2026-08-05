using System.Security.Claims;
using System.Text;
using Accounts.Api;
using Accounts.Api.Endpoints;
using Accounts.Domain;
using Accounts.Infrastructure;
using BuildingBlocks.Auth;
using BuildingBlocks.Http;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("accounts-api");
builder.Services.AddAccountsInfrastructure(builder.Configuration);
builder.Services.AddGrpc();

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
app.MapGet("/", () => "Accounts.Api — /api/accounts · gRPC AccountCheck");
app.MapHealthChecks("/health");
app.MapAccountEndpoints();

// Azure Event Grid đẩy event vào đây (push model) — kèm xử lý validation handshake.
app.MapEventGridWebhook("/webhooks/eventgrid", async (eventType, payload, ct) =>
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
    db.AuditEvents.Add(AuditEvent.From($"EventGrid:{eventType}", payload, null));
    await db.SaveChangesAsync(ct);
});

app.Run();

/// <summary>Cho integration test truy cập entry point (WebApplicationFactory).</summary>
public partial class Program;

namespace Accounts.Api.Endpoints
{
    internal static class ClaimsPrincipalExtensions
    {
        /// <summary>JWT subject = định danh người dùng, dùng làm chủ sở hữu tài khoản.</summary>
        public static string Subject(this ClaimsPrincipal user)
            => user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("JWT thiếu subject.");
    }
}

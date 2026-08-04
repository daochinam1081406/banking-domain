using System.Text;
using BuildingBlocks.Auth;
using BuildingBlocks.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Payments.Application;
using Payments.Domain.Exceptions;
using Payments.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
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

// Dev token (production: thay bằng OAuth2/OIDC identity provider)
app.MapPost("/token", (TokenRequest req, JwtOptions opt) =>
    Results.Ok(new { token = JwtTokenFactory.Issue(opt, req.Subject ?? "demo-user", req.Role ?? "customer") }));

app.MapPost("/api/transfers", async (
    InitiateTransferCommand cmd, InitiateTransferHandler handler, CancellationToken ct) =>
{
    try
    {
        var result = await handler.HandleAsync(cmd, ct);
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

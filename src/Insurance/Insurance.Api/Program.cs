using System.Security.Claims;
using System.Text;
using BuildingBlocks.Auth;
using BuildingBlocks.Http;
using BuildingBlocks.Observability;
using Insurance.Api.Endpoints;
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

// Tách quyền: chỉ giám định viên mới được duyệt/từ chối hồ sơ (segregation of duties).
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

await app.Services.MigrateInsuranceDatabaseAsync();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Insurance.Api — /api/policies · /api/claims");
app.MapHealthChecks("/health");
app.MapPolicyEndpoints();
app.MapClaimEndpoints();

app.Run();

/// <summary>Cho integration test truy cập entry point (WebApplicationFactory).</summary>
public partial class Program;

namespace Insurance.Api.Endpoints
{
    internal static class ClaimsPrincipalExtensions
    {
        /// <summary>JWT subject = định danh người dùng (chủ hợp đồng / giám định viên).</summary>
        public static string Subject(this ClaimsPrincipal user)
            => user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("JWT thiếu subject.");
    }
}

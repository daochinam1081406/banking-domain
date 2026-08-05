using System.Text;
using BuildingBlocks.Auth;
using BuildingBlocks.Http;
using BuildingBlocks.Observability;
using BuildingBlocks.State;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Payments.Api.Endpoints;
using Payments.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("payments-api");
builder.Services.AddPaymentsInfrastructure(builder.Configuration);

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
app.UseIdempotency();   // Idempotency-Key → client retry không tạo giao dịch trùng

using (var scope = app.Services.CreateScope())
{
    await SchemaInitializer.EnsureCreatedAsync(scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>());

    // Tài khoản demo — mật khẩu hash PBKDF2, KHÔNG lưu plaintext.
    await scope.ServiceProvider.GetRequiredService<IUserStore>().EnsureSeededAsync(
    [
        User.Create("demo",     "Demo@123",     Roles.Customer, "Nguyễn Văn Demo"),
        User.Create("alice",    "Alice@123",    Roles.Customer, "Trần Thị Alice"),
        User.Create("adjuster", "Adjuster@123", Roles.Adjuster, "Giám định viên Bảo Việt"),
    ]);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Payments.Api — /auth · /api/transfers");
app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapTransferEndpoints();

app.Run();

/// <summary>Cho integration test truy cập entry point (WebApplicationFactory).</summary>
public partial class Program;

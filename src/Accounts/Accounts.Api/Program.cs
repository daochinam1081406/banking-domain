using Accounts.Application;
using Accounts.Domain;
using Accounts.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAccountsInfrastructure(builder.Configuration);

var app = builder.Build();

// Áp migration + seed tài khoản demo khi khởi động.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
    db.Database.Migrate();
    AccountSeeder.Seed(db);
}

app.MapGet("/", () => "Accounts.Api — GET /api/accounts/{number}");
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "accounts" }));

app.MapGet("/api/accounts/{number}", async (string number, IAccountReadService reads, CancellationToken ct) =>
{
    var dto = await reads.GetByNumberAsync(number, ct);
    return dto is null ? Results.NotFound() : Results.Ok(dto);
});

app.MapPost("/api/accounts", async (OpenAccountRequest req, IAccountRepository repo, CancellationToken ct) =>
{
    var account = Account.Open(req.Number, req.InitialBalance, req.Currency ?? "VND");
    await repo.AddAsync(account, ct);
    await repo.SaveChangesAsync(ct);
    return Results.Created($"/api/accounts/{account.Number}",
        new { account.Number, account.Balance, account.Currency });
});

app.Run();

public sealed record OpenAccountRequest(string Number, decimal InitialBalance, string? Currency);

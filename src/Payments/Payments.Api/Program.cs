using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAzureServiceBus(builder.Configuration);

var app = builder.Build();

app.MapGet("/", () => "Payments.Api — POST /api/transfers");
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "payments" }));

// POST /api/transfers → publish MoneyTransferred lên Azure Service Bus.
// (Phase 2: persist Transfer aggregate + Outbox trong 1 transaction trước khi publish)
app.MapPost("/api/transfers", async (TransferRequest req, IEventBus bus, CancellationToken ct) =>
{
    if (req.Amount <= 0)
        return Results.BadRequest(new { error = "AMOUNT_INVALID", message = "Amount phải > 0." });
    if (string.IsNullOrWhiteSpace(req.FromAccount) || string.IsNullOrWhiteSpace(req.ToAccount))
        return Results.BadRequest(new { error = "ACCOUNT_REQUIRED" });
    if (req.FromAccount == req.ToAccount)
        return Results.BadRequest(new { error = "SAME_ACCOUNT" });

    var transferId = Guid.NewGuid();
    await bus.PublishAsync(new MoneyTransferredIntegrationEvent
    {
        TransferId  = transferId,
        FromAccount = req.FromAccount.Trim(),
        ToAccount   = req.ToAccount.Trim(),
        Amount      = req.Amount,
        Currency    = string.IsNullOrWhiteSpace(req.Currency) ? "VND" : req.Currency.Trim().ToUpperInvariant(),
    }, ct);

    return Results.Accepted($"/api/transfers/{transferId}",
        new { transferId, status = "Published" });
});

app.Run();

public sealed record TransferRequest(string FromAccount, string ToAccount, decimal Amount, string? Currency);

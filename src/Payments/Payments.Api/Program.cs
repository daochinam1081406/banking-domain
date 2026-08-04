using Npgsql;
using Payments.Application;
using Payments.Domain.Exceptions;
using Payments.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPaymentsInfrastructure(builder.Configuration);

var app = builder.Build();

// Tạo schema transfers + outbox khi khởi động.
using (var scope = app.Services.CreateScope())
{
    var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
    await SchemaInitializer.EnsureCreatedAsync(dataSource);
}

app.MapGet("/", () => "Payments.Api — POST /api/transfers");
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "payments" }));

// POST /api/transfers → Transfer + outbox trong 1 transaction; OutboxPublisher đẩy lên Service Bus.
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
});

app.MapGet("/api/transfers/{id:guid}", async (Guid id, ITransferReadService reads, CancellationToken ct) =>
{
    var dto = await reads.GetByIdAsync(id, ct);
    return dto is null ? Results.NotFound() : Results.Ok(dto);
});

app.Run();

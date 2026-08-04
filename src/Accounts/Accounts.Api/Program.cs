using Accounts.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<BuildingBlocks.Messaging.ServiceBusOptions>(
    builder.Configuration.GetSection("ServiceBus"));
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddHostedService<MoneyTransferConsumer>();

var app = builder.Build();

app.MapGet("/", () => "Accounts.Api — GET /api/accounts/{id}");
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "accounts" }));
app.MapGet("/api/accounts/{id}", (string id, AccountStore store) =>
    Results.Ok(new { accountId = id, balance = store.GetBalance(id) }));

app.Run();

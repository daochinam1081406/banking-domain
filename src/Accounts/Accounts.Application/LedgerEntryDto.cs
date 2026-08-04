namespace Accounts.Application;

public sealed record LedgerEntryDto(
    Guid Id, string AccountNumber, Guid TransferId, string Direction,
    decimal Amount, string Currency, decimal BalanceAfter, DateTimeOffset CreatedAt);

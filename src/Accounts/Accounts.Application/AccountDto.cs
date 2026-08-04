namespace Accounts.Application;

public sealed record AccountDto(string Number, decimal Balance, string Currency, DateTimeOffset UpdatedAt);

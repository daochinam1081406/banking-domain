namespace Accounts.Application;

public sealed record AccountDto(
    string Number, string OwnerId, decimal Balance, string Currency, DateTimeOffset UpdatedAt);

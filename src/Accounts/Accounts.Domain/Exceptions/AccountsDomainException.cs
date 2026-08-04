namespace Accounts.Domain.Exceptions;

public sealed class AccountsDomainException(string message, string errorCode) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}

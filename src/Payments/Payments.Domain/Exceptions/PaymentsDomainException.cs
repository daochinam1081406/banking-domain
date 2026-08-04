namespace Payments.Domain.Exceptions;

public sealed class PaymentsDomainException(string message, string errorCode) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}

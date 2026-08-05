namespace Insurance.Domain.Exceptions;

public sealed class InsuranceDomainException(string message, string errorCode) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}

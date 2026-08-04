using Payments.Domain;
using Payments.Domain.Exceptions;
using Xunit;

namespace BankingDomain.UnitTests;

public class TransferTests
{
    [Fact]
    public void Initiate_Valid_ShouldStartInitiated()
    {
        var t = Transfer.Initiate("acc-001", "acc-002", 500_000m);
        Assert.Equal("ACC-001", t.FromAccount);   // normalize upper
        Assert.Equal("ACC-002", t.ToAccount);
        Assert.Equal("Initiated", t.Status);
        Assert.Equal("VND", t.Currency);
    }

    [Fact]
    public void Initiate_SameAccount_ShouldThrow()
    {
        var ex = Assert.Throws<PaymentsDomainException>(() => Transfer.Initiate("ACC-001", "acc-001", 100m));
        Assert.Equal("SAME_ACCOUNT", ex.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Initiate_NonPositiveAmount_ShouldThrow(decimal amount)
    {
        var ex = Assert.Throws<PaymentsDomainException>(() => Transfer.Initiate("ACC-001", "ACC-002", amount));
        Assert.Equal("AMOUNT_INVALID", ex.ErrorCode);
    }

    [Fact]
    public void Initiate_MissingAccount_ShouldThrow()
    {
        var ex = Assert.Throws<PaymentsDomainException>(() => Transfer.Initiate("", "ACC-002", 100m));
        Assert.Equal("ACCOUNT_REQUIRED", ex.ErrorCode);
    }
}

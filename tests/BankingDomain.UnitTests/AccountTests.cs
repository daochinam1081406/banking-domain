using Accounts.Domain;
using Accounts.Domain.Exceptions;
using Xunit;

namespace BankingDomain.UnitTests;

public class AccountTests
{
    [Fact]
    public void Open_ShouldStartWithInitialBalance()
    {
        var acc = Account.Open("acc-001", "owner1", 1_000_000m);
        Assert.Equal("ACC-001", acc.Number);   // normalize upper
        Assert.Equal(1_000_000m, acc.Balance);
    }

    [Fact]
    public void Credit_ShouldIncreaseBalance()
    {
        var acc = Account.Open("ACC-001", "owner1", 100m);
        acc.Credit(50m);
        Assert.Equal(150m, acc.Balance);
    }

    [Fact]
    public void Debit_WithinBalance_ShouldDecrease()
    {
        var acc = Account.Open("ACC-001", "owner1", 100m);
        acc.Debit(40m);
        Assert.Equal(60m, acc.Balance);
    }

    [Fact]
    public void Debit_Overdraft_ShouldThrow()
    {
        var acc = Account.Open("ACC-001", "owner1", 100m);
        var ex = Assert.Throws<AccountsDomainException>(() => acc.Debit(150m));
        Assert.Equal("INSUFFICIENT_FUNDS", ex.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Credit_NonPositive_ShouldThrow(decimal amount)
    {
        var acc = Account.Open("ACC-001", "owner1", 100m);
        var ex = Assert.Throws<AccountsDomainException>(() => acc.Credit(amount));
        Assert.Equal("AMOUNT_INVALID", ex.ErrorCode);
    }

    [Fact]
    public void Open_NegativeInitial_ShouldThrow()
    {
        var ex = Assert.Throws<AccountsDomainException>(() => Account.Open("ACC-001", "owner1", -1m));
        Assert.Equal("BALANCE_INVALID", ex.ErrorCode);
    }
}

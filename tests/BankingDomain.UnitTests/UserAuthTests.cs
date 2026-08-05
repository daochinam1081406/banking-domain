using BuildingBlocks.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BankingDomain.UnitTests;

public class UserAuthTests
{
    [Fact]
    public void Create_ShouldHashPassword_NotStorePlaintext()
    {
        var u = User.Create("alice", "Alice@123", Roles.Customer, "Alice");

        Assert.DoesNotContain("Alice@123", u.PasswordHash);
        Assert.NotEqual("Alice@123", u.PasswordHash);
        Assert.False(string.IsNullOrWhiteSpace(u.Salt));
    }

    [Fact]
    public void SamePassword_DifferentUsers_ShouldHaveDifferentHash()
    {
        var a = User.Create("alice", "Same@123", Roles.Customer, "A");
        var b = User.Create("bob", "Same@123", Roles.Customer, "B");

        Assert.NotEqual(a.PasswordHash, b.PasswordHash);   // salt riêng → chống rainbow table
    }

    [Fact]
    public void VerifyPassword_ShouldAcceptCorrect_RejectWrong()
    {
        var u = User.Create("alice", "Alice@123", Roles.Customer, "Alice");

        Assert.True(u.VerifyPassword("Alice@123"));
        Assert.False(u.VerifyPassword("alice@123"));   // phân biệt hoa thường
        Assert.False(u.VerifyPassword("sai-mat-khau"));
    }

    [Fact]
    public void Create_ShortPassword_ShouldThrow()
        => Assert.Throws<ArgumentException>(() => User.Create("alice", "123", Roles.Customer, "Alice"));

    [Fact]
    public async Task Login_WrongPassword_ShouldThrowInvalidCredentials()
    {
        var users = new InMemoryUserStore(User.Create("alice", "Alice@123", Roles.Customer, "Alice"));
        var svc = new TokenService(
            new JwtOptions { Secret = new string('k', 48) },
            new InMemoryRefreshTokenStore(), users, NullLogger<TokenService>.Instance);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => svc.LoginAsync("alice", "sai"));
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => svc.LoginAsync("khong-ton-tai", "gi-do"));
    }

    [Fact]
    public async Task Login_ShouldCarryRoleIntoToken()
    {
        var users = new InMemoryUserStore(User.Create("adj", "Adj@1234", Roles.Adjuster, "Giám định"));
        var svc = new TokenService(
            new JwtOptions { Secret = new string('k', 48) },
            new InMemoryRefreshTokenStore(), users, NullLogger<TokenService>.Instance);

        var pair = await svc.LoginAsync("adj", "Adj@1234");

        // JWT payload chứa role adjuster
        var payload = pair.AccessToken.Split('.')[1];
        var json = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')));
        Assert.Contains(Roles.Adjuster, json);
    }
}

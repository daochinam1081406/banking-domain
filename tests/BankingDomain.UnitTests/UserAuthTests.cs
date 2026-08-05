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
            new InMemoryRefreshTokenStore(), users, new NoOpLoginThrottle(), NullLogger<TokenService>.Instance);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => svc.LoginAsync("alice", "sai"));
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => svc.LoginAsync("khong-ton-tai", "gi-do"));
    }

    [Fact]
    public async Task Login_ShouldCarryRoleIntoToken()
    {
        var users = new InMemoryUserStore(User.Create("adj", "Adj@1234", Roles.Adjuster, "Giám định"));
        var svc = new TokenService(
            new JwtOptions { Secret = new string('k', 48) },
            new InMemoryRefreshTokenStore(), users, new NoOpLoginThrottle(), NullLogger<TokenService>.Instance);

        var pair = await svc.LoginAsync("adj", "Adj@1234");

        // JWT payload chứa role adjuster
        var payload = pair.AccessToken.Split('.')[1];
        var json = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')));
        Assert.Contains(Roles.Adjuster, json);
    }
}

/// <summary>Chống brute-force: khoá theo tài khoản và theo IP.</summary>
public class LoginThrottleTests
{
    /// <summary>Throttle giả lập Redis — đếm in-memory, đủ để kiểm logic.</summary>
    private sealed class CountingThrottle(int maxPerAccount) : ILoginThrottle
    {
        private readonly Dictionary<string, int> _fails = [];
        public int ResetCount { get; private set; }

        public Task EnsureNotLockedAsync(string username, string? ip, CancellationToken ct = default)
        {
            if (_fails.GetValueOrDefault(username) >= maxPerAccount)
                throw new TooManyLoginAttemptsException("Quá nhiều lần sai.", TimeSpan.FromMinutes(15));
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(string username, string? ip, CancellationToken ct = default)
        {
            _fails[username] = _fails.GetValueOrDefault(username) + 1;
            return Task.CompletedTask;
        }

        public Task ResetAsync(string username, CancellationToken ct = default)
        {
            _fails.Remove(username); ResetCount++;
            return Task.CompletedTask;
        }
    }

    private static (TokenService Svc, CountingThrottle Throttle) Build(int max = 3)
    {
        var throttle = new CountingThrottle(max);
        var users = new InMemoryUserStore(User.Create("alice", "Alice@123", Roles.Customer, "Alice"));
        return (new TokenService(new JwtOptions { Secret = new string('k', 48) },
            new InMemoryRefreshTokenStore(), users, throttle, NullLogger<TokenService>.Instance), throttle);
    }

    [Fact]
    public async Task RepeatedWrongPassword_ShouldLockAccount()
    {
        var (svc, _) = Build(max: 3);

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<InvalidCredentialsException>(() => svc.LoginAsync("alice", "sai"));

        // Lần thứ 4: bị chặn TRƯỚC khi kiểm mật khẩu — kể cả mật khẩu đúng cũng không vào được.
        await Assert.ThrowsAsync<TooManyLoginAttemptsException>(() => svc.LoginAsync("alice", "Alice@123"));
    }

    [Fact]
    public async Task SuccessfulLogin_ShouldResetCounter()
    {
        var (svc, throttle) = Build(max: 3);
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => svc.LoginAsync("alice", "sai"));

        await svc.LoginAsync("alice", "Alice@123");

        Assert.Equal(1, throttle.ResetCount);
        await svc.LoginAsync("alice", "Alice@123");   // vẫn vào được, bộ đếm đã xoá
    }

    [Fact]
    public async Task UnknownUser_ShouldAlsoBeThrottled()
    {
        var (svc, _) = Build(max: 2);

        // Đếm cả tài khoản không tồn tại — nếu không sẽ lộ tài khoản nào có thật.
        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAsync<InvalidCredentialsException>(() => svc.LoginAsync("khong-ton-tai", "x"));

        await Assert.ThrowsAsync<TooManyLoginAttemptsException>(() => svc.LoginAsync("khong-ton-tai", "x"));
    }
}

/// <summary>Contract versioning — publisher và consumer deploy độc lập nên phải tự bảo vệ.</summary>
public class SchemaVersioningTests
{
    [Fact]
    public void NewEvent_ShouldDefaultToVersion1()
        => Assert.Equal(1, new BuildingBlocks.Contracts.MoneyTransferredIntegrationEvent
        {
            TransferId = Guid.NewGuid(), FromAccount = "A", ToAccount = "B",
            Amount = 1m, Currency = "VND",
        }.SchemaVersion);

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]    // publisher đã nâng cấp, consumer chưa → phải từ chối
    [InlineData(99, false)]
    [InlineData(0, false)]    // version rác
    public void IsSupported_ShouldAcceptOnlyKnownVersions(int version, bool expected)
        => Assert.Equal(expected, BuildingBlocks.Contracts.SchemaCompatibility.IsSupported(version));

    [Fact]
    public void Reason_ShouldExplainWhatToDo()
    {
        var reason = BuildingBlocks.Contracts.SchemaCompatibility.Reason(5);
        Assert.Contains("5", reason);
        Assert.Contains("nâng cấp", reason);   // thông báo phải nói rõ hành động cần làm
    }
}

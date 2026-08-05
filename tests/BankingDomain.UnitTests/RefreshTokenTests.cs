using BuildingBlocks.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BankingDomain.UnitTests;

internal sealed class InMemoryUserStore(params User[] users) : IUserStore
{
    private readonly Dictionary<string, User> _users = users.ToDictionary(u => u.Username);
    public Task<User?> FindAsync(string username, CancellationToken ct = default)
        => Task.FromResult(_users.GetValueOrDefault(username.Trim().ToLowerInvariant()));
    public Task EnsureSeededAsync(IEnumerable<User> seed, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly Dictionary<string, RefreshToken> _tokens = [];

    public Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        _tokens[token.TokenHash] = token;
        return Task.CompletedTask;
    }

    public Task<RefreshToken?> FindAsync(string tokenHash, CancellationToken ct = default)
        => Task.FromResult(_tokens.GetValueOrDefault(tokenHash));

    public Task RevokeFamilyAsync(string familyId, CancellationToken ct = default)
    {
        foreach (var t in _tokens.Values.Where(t => t.FamilyId == familyId)) t.Revoke();
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    public int ActiveCount => _tokens.Values.Count(t => t.IsActive);
}

public class RefreshTokenTests
{
    private static (TokenService Svc, InMemoryRefreshTokenStore Store) Build()
    {
        var store = new InMemoryRefreshTokenStore();
        var jwt = new JwtOptions { Secret = new string('k', 48), ExpiryMinutes = 30 };
        var users = new InMemoryUserStore(User.Create("alice", "Alice@123", Roles.Customer, "Alice"));
        return (new TokenService(jwt, store, users, new NoOpLoginThrottle(), NullLogger<TokenService>.Instance), store);
    }

    [Fact]
    public async Task Login_ShouldIssueAccessAndRefreshToken()
    {
        var (svc, _) = Build();
        var pair = await svc.LoginAsync("alice", "Alice@123");

        Assert.False(string.IsNullOrWhiteSpace(pair.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(pair.RefreshToken));
        Assert.Equal(30 * 60, pair.ExpiresInSeconds);
    }

    [Fact]
    public async Task Refresh_ShouldRotate_OldTokenNoLongerUsable()
    {
        var (svc, _) = Build();
        var first = await svc.LoginAsync("alice", "Alice@123");

        var second = await svc.RefreshAsync(first.RefreshToken);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);   // rotation

        // Token cũ dùng lại → reuse detection
        await Assert.ThrowsAsync<RefreshTokenReuseException>(() => svc.RefreshAsync(first.RefreshToken));
    }

    [Fact]
    public async Task Reuse_ShouldRevokeWholeFamily()
    {
        var (svc, store) = Build();
        var first = await svc.LoginAsync("alice", "Alice@123");
        var second = await svc.RefreshAsync(first.RefreshToken);

        Assert.Equal(1, store.ActiveCount);   // chỉ token mới còn sống

        await Assert.ThrowsAsync<RefreshTokenReuseException>(() => svc.RefreshAsync(first.RefreshToken));

        // Nghi bị đánh cắp → huỷ cả phiên, token "mới" cũng chết
        Assert.Equal(0, store.ActiveCount);
        await Assert.ThrowsAsync<RefreshTokenReuseException>(() => svc.RefreshAsync(second.RefreshToken));
    }

    [Fact]
    public async Task Refresh_UnknownToken_ShouldThrowInvalid()
    {
        var (svc, _) = Build();
        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() => svc.RefreshAsync("khong-ton-tai"));
    }

    [Fact]
    public async Task Logout_ShouldRevokeFamily()
    {
        var (svc, store) = Build();
        var pair = await svc.LoginAsync("alice", "Alice@123");

        await svc.LogoutAsync(pair.RefreshToken);

        Assert.Equal(0, store.ActiveCount);
        await Assert.ThrowsAsync<RefreshTokenReuseException>(() => svc.RefreshAsync(pair.RefreshToken));
    }

    [Fact]
    public void TokenHash_ShouldNotStoreRawToken()
    {
        var (entity, raw) = RefreshToken.Issue("alice", RefreshToken.NewFamilyId());

        Assert.NotEqual(raw, entity.TokenHash);              // DB không giữ token gốc
        Assert.Equal(RefreshToken.Hash(raw), entity.TokenHash);
        Assert.Equal(64, entity.TokenHash.Length);           // SHA-256 hex
    }
}

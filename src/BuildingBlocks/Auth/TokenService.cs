using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Auth;

public sealed record TokenPair(string AccessToken, string RefreshToken, int ExpiresInSeconds);

public sealed class RefreshTokenReuseException(string message) : Exception(message)
{
    public string ErrorCode => "REFRESH_TOKEN_REUSED";
}

public sealed class InvalidRefreshTokenException(string message) : Exception(message)
{
    public string ErrorCode => "REFRESH_TOKEN_INVALID";
}

/// <summary>
/// Cấp + xoay refresh token.
/// - `LoginAsync` mở family mới.
/// - `RefreshAsync` **rotation**: mỗi lần refresh cấp cặp mới và revoke token cũ.
/// - **Reuse detection**: token đã revoke bị dùng lại ⇒ token bị đánh cắp ⇒ revoke CẢ family
///   (buộc đăng nhập lại) — đúng khuyến nghị OWASP.
/// </summary>
public sealed class TokenService(
    JwtOptions jwtOptions,
    IRefreshTokenStore store,
    ILogger<TokenService> logger)
{
    public async Task<TokenPair> LoginAsync(string subject, string role = "customer", CancellationToken ct = default)
    {
        var (entity, raw) = RefreshToken.Issue(subject, RefreshToken.NewFamilyId());
        await store.AddAsync(entity, ct);
        await store.SaveChangesAsync(ct);
        return Pair(subject, role, raw);
    }

    public async Task<TokenPair> RefreshAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var hash = RefreshToken.Hash(rawRefreshToken);
        var existing = await store.FindAsync(hash, ct)
            ?? throw new InvalidRefreshTokenException("Refresh token không hợp lệ.");

        if (existing.RevokedAt is not null)
        {
            // Token đã xoay trước đó lại được dùng → nghi bị đánh cắp → huỷ toàn bộ phiên.
            logger.LogWarning("Refresh token reuse detected cho subject {Subject}, revoke family {Family}",
                existing.Subject, existing.FamilyId);
            await store.RevokeFamilyAsync(existing.FamilyId, ct);
            await store.SaveChangesAsync(ct);
            throw new RefreshTokenReuseException("Refresh token đã bị dùng lại — mọi phiên đã bị thu hồi.");
        }

        if (!existing.IsActive)
            throw new InvalidRefreshTokenException("Refresh token đã hết hạn.");

        existing.Revoke();                                  // rotation: token cũ chết ngay
        var (entity, raw) = RefreshToken.Issue(existing.Subject, existing.FamilyId);
        await store.AddAsync(entity, ct);
        await store.SaveChangesAsync(ct);

        return Pair(existing.Subject, "customer", raw);
    }

    public async Task LogoutAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var existing = await store.FindAsync(RefreshToken.Hash(rawRefreshToken), ct);
        if (existing is null) return;
        await store.RevokeFamilyAsync(existing.FamilyId, ct);
        await store.SaveChangesAsync(ct);
    }

    private TokenPair Pair(string subject, string role, string refreshToken)
        => new(JwtTokenFactory.Issue(jwtOptions, subject, role), refreshToken, jwtOptions.ExpiryMinutes * 60);
}

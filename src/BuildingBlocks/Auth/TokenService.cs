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

public sealed class InvalidCredentialsException(string message) : Exception(message)
{
    public string ErrorCode => "INVALID_CREDENTIALS";
}

/// <summary>
/// Cấp + xoay refresh token.
/// - `LoginAsync` mở family mới.
/// - `RefreshAsync` rotation: mỗi lần refresh cấp cặp mới và revoke token cũ.
/// - Reuse detection: token đã revoke bị dùng lại ⇒ token bị đánh cắp ⇒ revoke CẢ family
///   (buộc đăng nhập lại) — đúng khuyến nghị OWASP.
/// </summary>
public sealed class TokenService(
    JwtOptions jwtOptions,
    IRefreshTokenStore store,
    IUserStore users,
    ILoginThrottle throttle,
    ILogger<TokenService> logger)
{
    /// <summary>Xác thực username/password (PBKDF2), có chống brute-force theo tài khoản + IP.</summary>
    public async Task<TokenPair> LoginAsync(
        string username, string password, string? ipAddress = null, CancellationToken ct = default)
    {
        // Kiểm tra khoá TRƯỚC khi tra DB/băm mật khẩu — vừa chặn sớm, vừa không tốn CPU cho attacker.
        await throttle.EnsureNotLockedAsync(username, ipAddress, ct);

        var user = await users.FindAsync(username, ct);

        // Cùng một thông báo cho "sai user" và "sai mật khẩu" — không tiết lộ user nào tồn tại.
        if (user is null || !user.IsActive || !user.VerifyPassword(password))
        {
            await throttle.RecordFailureAsync(username, ipAddress, ct);
            logger.LogWarning("Đăng nhập thất bại cho {Username}", username);
            throw new InvalidCredentialsException("Tên đăng nhập hoặc mật khẩu không đúng.");
        }

        await throttle.ResetAsync(username, ct);

        var (entity, raw) = RefreshToken.Issue(user.Username, RefreshToken.NewFamilyId());
        await store.AddAsync(entity, ct);
        await store.SaveChangesAsync(ct);
        return Pair(user.Username, user.Role, raw);
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

        // Role lấy lại từ user store — tránh token cũ giữ role đã bị thu hồi.
        var user = await users.FindAsync(existing.Subject, ct);
        return Pair(existing.Subject, user?.Role ?? Roles.Customer, raw);
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

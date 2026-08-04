using System.Security.Cryptography;
using System.Text;

namespace BuildingBlocks.Auth;

/// <summary>
/// Refresh token với **rotation** + **reuse detection** (chuẩn OWASP cho public client).
/// DB chỉ lưu SHA-256 hash — lộ DB không dùng lại được token.
/// </summary>
public sealed class RefreshToken
{
    public string TokenHash { get; private set; } = null!;
    public string Subject { get; private set; } = null!;
    public string FamilyId { get; private set; } = null!;   // nhóm token cùng 1 phiên đăng nhập
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;

    private RefreshToken() { }

    public static (RefreshToken Entity, string RawToken) Issue(string subject, string familyId, int daysValid = 14)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        return (new RefreshToken
        {
            TokenHash = Hash(raw),
            Subject = subject,
            FamilyId = familyId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(daysValid),
        }, raw);
    }

    /// <summary>Dựng lại từ dữ liệu đã lưu (persistence layer gọi) — không dùng reflection.</summary>
    public static RefreshToken Rehydrate(
        string tokenHash, string subject, string familyId,
        DateTimeOffset expiresAt, DateTimeOffset? revokedAt) => new()
    {
        TokenHash = tokenHash,
        Subject = subject,
        FamilyId = familyId,
        ExpiresAt = expiresAt,
        RevokedAt = revokedAt,
    };

    public void Revoke() => RevokedAt ??= DateTimeOffset.UtcNow;

    public static string Hash(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    public static string NewFamilyId() => Guid.NewGuid().ToString("N");
}

public interface IRefreshTokenStore
{
    Task AddAsync(RefreshToken token, CancellationToken ct = default);
    Task<RefreshToken?> FindAsync(string tokenHash, CancellationToken ct = default);
    /// <summary>Revoke toàn bộ token cùng family — dùng khi phát hiện reuse (token đã revoke bị dùng lại).</summary>
    Task RevokeFamilyAsync(string familyId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

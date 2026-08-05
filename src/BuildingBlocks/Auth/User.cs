using System.Security.Cryptography;

namespace BuildingBlocks.Auth;

public static class Roles
{
    public const string Customer = "customer";   // chủ tài khoản / chủ hợp đồng
    public const string Adjuster = "adjuster";   // giám định viên — người duyệt bồi thường
}

/// <summary>
/// Tài khoản đăng nhập. Mật khẩu lưu **PBKDF2-SHA256 600k vòng** (khuyến nghị OWASP 2023+),
/// không bao giờ lưu plaintext. So sánh hash bằng hàm chống timing-attack.
/// </summary>
public sealed class User
{
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public string Username { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string Salt { get; private set; } = null!;
    public string Role { get; private set; } = Roles.Customer;
    public string DisplayName { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

    private User() { }

    public static User Create(string username, string password, string role, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        if (password.Length < 6)
            throw new ArgumentException("Mật khẩu tối thiểu 6 ký tự.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new User
        {
            Username = username.Trim().ToLowerInvariant(),
            Salt = Convert.ToBase64String(salt),
            PasswordHash = Hash(password, salt),
            Role = role,
            DisplayName = displayName,
            IsActive = true,
        };
    }

    public static User Rehydrate(string username, string passwordHash, string salt, string role,
        string displayName, bool isActive) => new()
    {
        Username = username,
        PasswordHash = passwordHash,
        Salt = salt,
        Role = role,
        DisplayName = displayName,
        IsActive = isActive,
    };

    public bool VerifyPassword(string password)
    {
        var computed = Hash(password, Convert.FromBase64String(Salt));
        // Fixed-time compare — tránh lộ thông tin qua thời gian phản hồi.
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(computed), Convert.FromBase64String(PasswordHash));
    }

    private static string Hash(string password, byte[] salt)
        => Convert.ToBase64String(
            Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize));
}

public interface IUserStore
{
    Task<User?> FindAsync(string username, CancellationToken ct = default);
    Task EnsureSeededAsync(IEnumerable<User> users, CancellationToken ct = default);
}

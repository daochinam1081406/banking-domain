using BuildingBlocks.Auth;
using Dapper;
using Npgsql;

namespace Payments.Infrastructure;

public sealed class DapperUserStore(NpgsqlDataSource dataSource) : IUserStore
{
    public async Task<User?> FindAsync(string username, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            """
            SELECT username AS Username, password_hash AS PasswordHash, salt AS Salt,
                   role AS Role, display_name AS DisplayName, is_active AS IsActive
            FROM users WHERE username = @U
            """, new { U = username.Trim().ToLowerInvariant() }, cancellationToken: ct));

        return row is null ? null
            : User.Rehydrate(row.Username, row.PasswordHash, row.Salt, row.Role, row.DisplayName, row.IsActive);
    }

    /// <summary>Seed tài khoản demo — idempotent, không ghi đè mật khẩu đã có.</summary>
    public async Task EnsureSeededAsync(IEnumerable<User> users, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        foreach (var u in users)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO users (username, password_hash, salt, role, display_name, is_active)
                VALUES (@Username, @PasswordHash, @Salt, @Role, @DisplayName, TRUE)
                ON CONFLICT (username) DO NOTHING
                """,
                new { u.Username, u.PasswordHash, u.Salt, u.Role, u.DisplayName }, cancellationToken: ct));
        }
    }

    private sealed record UserRow(
        string Username, string PasswordHash, string Salt, string Role, string DisplayName, bool IsActive);
}

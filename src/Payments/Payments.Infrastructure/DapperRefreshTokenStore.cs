using BuildingBlocks.Auth;
using Dapper;
using Npgsql;

namespace Payments.Infrastructure;

/// <summary>
/// Lưu refresh token (chỉ hash) trên Postgres. Thay đổi được gom rồi flush ở SaveChangesAsync
/// để giữ đúng ngữ nghĩa "rotation + revoke family là một đơn vị công việc".
/// </summary>
public sealed class DapperRefreshTokenStore(NpgsqlDataSource dataSource) : IRefreshTokenStore
{
    private readonly List<RefreshToken> _pendingAdds = [];
    private readonly List<string> _pendingFamilyRevokes = [];
    private readonly List<string> _pendingTokenRevokes = [];
    private readonly Dictionary<string, RefreshToken> _loaded = [];

    public Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        _pendingAdds.Add(token);
        return Task.CompletedTask;
    }

    public async Task<RefreshToken?> FindAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<TokenRow>(new CommandDefinition(
            """
            SELECT token_hash AS TokenHash, subject AS Subject, family_id AS FamilyId,
                   expires_at AS ExpiresAt, revoked_at AS RevokedAt
            FROM refresh_tokens WHERE token_hash = @Hash
            """, new { Hash = tokenHash }, cancellationToken: ct));
        if (row is null) return null;

        var entity = row.ToEntity();
        _loaded[row.TokenHash] = entity;
        return entity;
    }

    public Task RevokeFamilyAsync(string familyId, CancellationToken ct = default)
    {
        _pendingFamilyRevokes.Add(familyId);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        // Token được Revoke() trong domain → ghi nhận để UPDATE.
        foreach (var (hash, entity) in _loaded)
            if (entity.RevokedAt is not null) _pendingTokenRevokes.Add(hash);

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        foreach (var familyId in _pendingFamilyRevokes)
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE refresh_tokens SET revoked_at = NOW() WHERE family_id = @F AND revoked_at IS NULL",
                new { F = familyId }, tx, cancellationToken: ct));

        foreach (var hash in _pendingTokenRevokes)
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE refresh_tokens SET revoked_at = NOW() WHERE token_hash = @H AND revoked_at IS NULL",
                new { H = hash }, tx, cancellationToken: ct));

        foreach (var t in _pendingAdds)
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO refresh_tokens (token_hash, subject, family_id, expires_at, revoked_at)
                VALUES (@TokenHash, @Subject, @FamilyId, @ExpiresAt, NULL)
                ON CONFLICT (token_hash) DO NOTHING
                """,
                new { t.TokenHash, t.Subject, t.FamilyId, t.ExpiresAt }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        _pendingAdds.Clear();
        _pendingFamilyRevokes.Clear();
        _pendingTokenRevokes.Clear();
        _loaded.Clear();
    }

    private sealed record TokenRow(
        string TokenHash, string Subject, string FamilyId,
        DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt)
    {
        public RefreshToken ToEntity()
            => RefreshToken.Rehydrate(TokenHash, Subject, FamilyId, ExpiresAt, RevokedAt);
    }
}

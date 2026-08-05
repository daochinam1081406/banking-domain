using System.Security.Claims;
using BuildingBlocks.Auth;

namespace Payments.Api.Endpoints;

/// <summary>
/// Auth tập trung: đăng nhập username/password (PBKDF2) + refresh token rotation.
/// Các service khác chỉ *validate* JWT bằng chung secret, không tự phát token.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/auth").WithTags("Auth");

        g.MapPost("/login", Login).WithName("Login");
        g.MapPost("/refresh", Refresh).WithName("Refresh");
        g.MapPost("/logout", Logout).WithName("Logout");
        g.MapGet("/me", Me).WithName("Me").RequireAuthorization();

        return app;
    }

    private static async Task<IResult> Login(LoginRequest req, TokenService tokens, CancellationToken ct)
        => Results.Ok(await tokens.LoginAsync(req.Username, req.Password, ct));

    private static async Task<IResult> Refresh(RefreshRequest req, TokenService tokens, CancellationToken ct)
        => Results.Ok(await tokens.RefreshAsync(req.RefreshToken, ct));

    private static async Task<IResult> Logout(RefreshRequest req, TokenService tokens, CancellationToken ct)
    {
        await tokens.LogoutAsync(req.RefreshToken, ct);
        return Results.NoContent();
    }

    private static IResult Me(ClaimsPrincipal user) => Results.Ok(new
    {
        username = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value,
        role = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value,
    });
}

public sealed record LoginRequest(string Username, string Password);
public sealed record RefreshRequest(string RefreshToken);

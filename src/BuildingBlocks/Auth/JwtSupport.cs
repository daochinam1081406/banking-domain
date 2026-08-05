using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Auth;

public sealed class JwtOptions
{
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "banking-domain";
    public string Audience { get; set; } = "banking-clients";
    public int ExpiryMinutes { get; set; } = 60;
}

/// <summary>
/// Sinh JWT HS256 (pure — không phụ thuộc ASP.NET). Dùng cho dev /token endpoint.
/// Validation cấu hình ở tầng Api (JwtBearer). OAuth2/OIDC flow thật thay bằng IdP ở production.
/// </summary>
public static class JwtTokenFactory
{
    public static string Issue(JwtOptions opt, string subject, string role = "customer")
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, subject),
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: opt.Issuer,
            audience: opt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(opt.ExpiryMinutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

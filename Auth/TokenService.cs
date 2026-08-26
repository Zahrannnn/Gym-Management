using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Gym_Management.Domain;
using Microsoft.IdentityModel.Tokens;

namespace Gym_Management.Auth;

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) CreateToken(StaffUser user);
}

/// <summary>
/// Issues HMAC-SHA256 signed JWTs for staff users. The symmetric key comes from
/// Jwt:Key (>= 32 chars) with Jwt:Issuer / Jwt:Audience; the role claim drives
/// [Authorize(Roles = ...)] enforcement.
/// </summary>
public class TokenService(IConfiguration configuration) : ITokenService
{
    public const int MinimumKeyLength = 32;
    public const double DefaultExpiresHours = 8;

    /// <summary>
    /// IdentityModel 7 rejects kid-less tokens signed with a symmetric key (IDX10517),
    /// so both issuance and validation use the same key id, which lands in the JWT kid header.
    /// </summary>
    public const string SigningKeyId = "gym-management-jwt-v1";

    public static SymmetricSecurityKey CreateSigningKey(string secret) =>
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) { KeyId = SigningKeyId };

    public (string Token, DateTime ExpiresAtUtc) CreateToken(StaffUser user)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddHours(GetExpiresHours());

        var key = GetSigningKey();
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            // ClaimTypes.Role is serialized as the JWT "role" claim and mapped back on
            // validation, which is what the authorization middleware matches roles against.
            new(ClaimTypes.Role, user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    private SymmetricSecurityKey GetSigningKey()
    {
        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < MinimumKeyLength)
        {
            throw new InvalidOperationException($"Jwt:Key must be configured with at least {MinimumKeyLength} characters.");
        }

        return CreateSigningKey(key);
    }

    private double GetExpiresHours()
    {
        var raw = configuration["Jwt:ExpiresHours"];
        return double.TryParse(raw, out var hours) && hours > 0 ? hours : DefaultExpiresHours;
    }
}

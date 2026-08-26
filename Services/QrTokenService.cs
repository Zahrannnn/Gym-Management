using System.Security.Cryptography;
using System.Text;

namespace Gym_Management.Services;

public interface IQrTokenService
{
    /// <summary>128-bit crypto-random, Base64Url-encoded (~22 chars). Returned once; never persisted.</summary>
    string GenerateRawToken();

    /// <summary>SHA-256 of the UTF-8 raw token, Base64Url-encoded for storage/lookup.</summary>
    string HashToken(string rawToken);
}

public class QrTokenService : IQrTokenService
{
    public string GenerateRawToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    public string HashToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

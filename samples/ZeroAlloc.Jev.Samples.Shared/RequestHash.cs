using System.Security.Cryptography;

namespace ZeroAlloc.Jev.Samples;

/// <summary>Identifies a request by its body: the lowercase hex SHA-256 of the exact bytes sent.</summary>
public static class RequestHash
{
    public static string Of(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Convert.ToHexStringLower(SHA256.HashData(body));
    }
}

using System.Security.Cryptography;

namespace Eam.Core.Security;

/// <summary>PBKDF2-SHA256 password hashing. Format: pbkdf2$iterations$salt$hash (base64).</summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out var it) || it < 1000)
            return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, it, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Minimum rules, kept simple so admins are not pushed to write passwords down.</summary>
    public static string? CheckStrength(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 10) return "password_too_short";
        if (password.Length > 200) return "password_too_long";
        return null;
    }
}

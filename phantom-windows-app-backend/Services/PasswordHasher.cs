using System.Security.Cryptography;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private readonly int _iterations;

    public PasswordHasher(int iterations)
    {
        _iterations = iterations;
    }

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, KeySize);
        return $"pbkdf2-sha256${_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string encodedHash)
    {
        if (string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split('$', StringSplitOptions.None);
        if (parts.Length != 4 || !string.Equals(parts[0], "pbkdf2-sha256", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

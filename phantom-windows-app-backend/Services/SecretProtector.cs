using System.Security.Cryptography;
using System.Text;
using Phantom.WindowsApp.Backend.Infrastructure;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class SecretProtector
{
    private readonly byte[] _keyBytes;

    public SecretProtector(BackendOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SecretEncryptionKey))
        {
            throw new InvalidOperationException(
                "PHANTOM_WINDOWS_BACKEND_SECRET_ENCRYPTION_KEY is required for production secret storage.");
        }

        try
        {
            _keyBytes = Convert.FromBase64String(options.SecretEncryptionKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "PHANTOM_WINDOWS_BACKEND_SECRET_ENCRYPTION_KEY must be a base64-encoded 32-byte key.",
                ex);
        }

        if (_keyBytes.Length != 32)
        {
            throw new InvalidOperationException(
                "PHANTOM_WINDOWS_BACKEND_SECRET_ENCRYPTION_KEY must decode to exactly 32 bytes.");
        }
    }

    public string Protect(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_keyBytes);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        return $"{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(tag)}.{Convert.ToBase64String(ciphertext)}";
    }

    public string Unprotect(string protectedValue)
    {
        var parts = protectedValue.Split('.', StringSplitOptions.None);
        if (parts.Length != 3)
        {
            throw new InvalidOperationException("Stored protected secret format is invalid.");
        }

        var nonce = Convert.FromBase64String(parts[0]);
        var tag = Convert.FromBase64String(parts[1]);
        var ciphertext = Convert.FromBase64String(parts[2]);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_keyBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}

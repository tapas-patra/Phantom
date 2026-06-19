using System.Security.Cryptography;
using System.Text;
using Phantom.Dashboard.Backend.Persistence;

namespace Phantom.Dashboard.Backend.Services;

public sealed class AdminSessionValidator
{
    private readonly PostgresDashboardStore _store;

    public AdminSessionValidator(PostgresDashboardStore store)
    {
        _store = store;
    }

    public bool IsValid(string authorizationHeader)
    {
        var accessToken = ParseBearerToken(authorizationHeader);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT 1
FROM auth_sessions s
JOIN admin_accounts a ON a.admin_id = s.user_id
WHERE s.access_token_hash = @accessTokenHash
  AND s.is_authenticated = TRUE
  AND s.revoked_at_utc IS NULL
  AND s.expires_at_utc > @now
  AND a.is_active = TRUE
  AND s.auth_method LIKE 'admin:%'
LIMIT 1;";
        command.Parameters.AddWithValue("accessTokenHash", HashToken(accessToken));
        command.Parameters.AddWithValue("now", DateTime.UtcNow);
        return command.ExecuteScalar() != null;
    }

    private static string ParseBearerToken(string authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return authorizationHeader["Bearer ".Length..].Trim();
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}

using Npgsql;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class LoginAttemptRepository
{
    private readonly PostgresBackendStore _store;

    public LoginAttemptRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public int CountRecentFailures(string email, string ipAddress, DateTime sinceUtc)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT COUNT(*) FROM auth_login_attempts
WHERE lower(email) = lower(@email)
  AND ip_address = @ipAddress
  AND attempted_at_utc >= @sinceUtc
  AND succeeded = FALSE;";
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("ipAddress", ipAddress);
        command.Parameters.AddWithValue("sinceUtc", sinceUtc);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    public void Record(string email, string ipAddress, bool succeeded)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO auth_login_attempts (
    attempt_id, email, ip_address, attempted_at_utc, succeeded
) VALUES (
    @attemptId, @email, @ipAddress, @attemptedAtUtc, @succeeded
);";
        command.Parameters.AddWithValue("attemptId", $"attempt-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("ipAddress", ipAddress);
        command.Parameters.AddWithValue("attemptedAtUtc", DateTime.UtcNow);
        command.Parameters.AddWithValue("succeeded", succeeded);
        command.ExecuteNonQuery();
    }

    public void DeleteExpired(DateTime cutoffUtc)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM auth_login_attempts WHERE attempted_at_utc < @cutoffUtc;";
        command.Parameters.AddWithValue("cutoffUtc", cutoffUtc);
        command.ExecuteNonQuery();
    }
}

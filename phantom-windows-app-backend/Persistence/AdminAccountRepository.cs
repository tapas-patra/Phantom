using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class AdminAccountRepository
{
    private readonly PostgresBackendStore _store;

    public AdminAccountRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public AdminAccountRecord? FindByAdminId(string adminId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM admin_accounts WHERE admin_id = @adminId LIMIT 1;";
        command.Parameters.AddWithValue("adminId", adminId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public AdminAccountRecord? FindByEmail(string email)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM admin_accounts WHERE lower(email) = lower(@email) LIMIT 1;";
        command.Parameters.AddWithValue("email", email);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public bool ExistsAny()
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM admin_accounts LIMIT 1;";
        return command.ExecuteScalar() != null;
    }

    public void Save(AdminAccountRecord account)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO admin_accounts (
    admin_id, email, display_name, role, password_hash, is_active, last_login_at_utc, created_at_utc, updated_at_utc
) VALUES (
    @adminId, @email, @displayName, @role, @passwordHash, @isActive, @lastLoginAtUtc, @createdAtUtc, @updatedAtUtc
)
ON CONFLICT(admin_id) DO UPDATE SET
    email = EXCLUDED.email,
    display_name = EXCLUDED.display_name,
    role = EXCLUDED.role,
    password_hash = EXCLUDED.password_hash,
    is_active = EXCLUDED.is_active,
    last_login_at_utc = EXCLUDED.last_login_at_utc,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        Bind(command, account);
        command.ExecuteNonQuery();
    }

    private static void Bind(NpgsqlCommand command, AdminAccountRecord account)
    {
        command.Parameters.AddWithValue("adminId", account.AdminId);
        command.Parameters.AddWithValue("email", account.Email);
        command.Parameters.AddWithValue("displayName", account.DisplayName);
        command.Parameters.AddWithValue("role", account.Role);
        command.Parameters.AddWithValue("passwordHash", account.PasswordHash);
        command.Parameters.AddWithValue("isActive", account.IsActive);
        command.Parameters.AddWithValue("lastLoginAtUtc", (object?)account.LastLoginAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("createdAtUtc", account.CreatedAtUtc);
        command.Parameters.AddWithValue("updatedAtUtc", account.UpdatedAtUtc);
    }

    private static AdminAccountRecord Map(NpgsqlDataReader reader)
    {
        var lastLoginOrdinal = reader.GetOrdinal("last_login_at_utc");
        return new AdminAccountRecord
        {
            AdminId = reader.GetString(reader.GetOrdinal("admin_id")),
            Email = reader.GetString(reader.GetOrdinal("email")),
            DisplayName = reader.GetString(reader.GetOrdinal("display_name")),
            Role = reader.GetString(reader.GetOrdinal("role")),
            PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
            IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
            LastLoginAtUtc = reader.IsDBNull(lastLoginOrdinal) ? null : reader.GetDateTime(lastLoginOrdinal),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }
}

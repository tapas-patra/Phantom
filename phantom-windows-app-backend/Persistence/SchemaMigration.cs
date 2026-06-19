using Npgsql;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed record SchemaMigration(string Id, string Sql);

public static class SchemaMigrator
{
    private const long MigrationLockId = 438921;

    public static void ApplyMigrations(NpgsqlConnection connection, IReadOnlyList<SchemaMigration> migrations)
    {
        AcquireMigrationLock(connection);
        try
        {
            EnsureHistoryTable(connection);

            foreach (var migration in migrations)
            {
                if (HasMigration(connection, migration.Id))
                {
                    continue;
                }

                using var transaction = connection.BeginTransaction();
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = migration.Sql;
                    command.ExecuteNonQuery();
                }

                using (var historyCommand = connection.CreateCommand())
                {
                    historyCommand.Transaction = transaction;
                    historyCommand.CommandText = @"
INSERT INTO __schema_migrations (migration_id, applied_at_utc)
VALUES (@migrationId, NOW());";
                    historyCommand.Parameters.AddWithValue("migrationId", migration.Id);
                    historyCommand.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }
        finally
        {
            ReleaseMigrationLock(connection);
        }
    }

    private static void EnsureHistoryTable(NpgsqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS __schema_migrations (
    migration_id TEXT PRIMARY KEY,
    applied_at_utc TIMESTAMPTZ NOT NULL
);";
        command.ExecuteNonQuery();
    }

    private static bool HasMigration(NpgsqlConnection connection, string migrationId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM __schema_migrations WHERE migration_id = @migrationId LIMIT 1;";
        command.Parameters.AddWithValue("migrationId", migrationId);
        return command.ExecuteScalar() != null;
    }

    private static void AcquireMigrationLock(NpgsqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_lock(@lockId);";
        command.Parameters.AddWithValue("lockId", MigrationLockId);
        command.ExecuteNonQuery();
    }

    private static void ReleaseMigrationLock(NpgsqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(@lockId);";
        command.Parameters.AddWithValue("lockId", MigrationLockId);
        command.ExecuteNonQuery();
    }
}

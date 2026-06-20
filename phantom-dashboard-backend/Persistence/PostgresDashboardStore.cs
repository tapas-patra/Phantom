using Npgsql;
using NpgsqlTypes;
using Phantom.Dashboard.Backend.Infrastructure;

namespace Phantom.Dashboard.Backend.Persistence;

public sealed class PostgresDashboardStore
{
    private readonly string _connectionString;

    public PostgresDashboardStore(DashboardOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DatabaseUrl))
        {
            throw new InvalidOperationException(
                "Dashboard database URL is not configured. Set PHANTOM_DASHBOARD_BACKEND_DATABASE_URL.");
        }

        _connectionString = BuildConnectionString(options.DatabaseUrl);
        using var connection = OpenConnection();
        SchemaMigrator.ApplyMigrations(connection, DashboardSchemaMigrations.All);
    }

    public NpgsqlConnection OpenConnection()
    {
        var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public bool CanConnect()
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            command.ExecuteScalar();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildConnectionString(string databaseUrl)
    {
        if (databaseUrl.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
        {
            var hostBuilder = new NpgsqlConnectionStringBuilder(databaseUrl);
            ApplyRecommendedDefaults(hostBuilder);
            return hostBuilder.ConnectionString;
        }

        if (databaseUrl.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || databaseUrl.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var withoutScheme = databaseUrl
                .Replace("postgresql://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("postgres://", string.Empty, StringComparison.OrdinalIgnoreCase);

            var atIndex = withoutScheme.LastIndexOf('@');
            if (atIndex <= 0 || atIndex >= withoutScheme.Length - 1)
            {
                throw new InvalidOperationException("Dashboard database URL must include credentials and host.");
            }

            var userInfo = withoutScheme[..atIndex];
            var hostAndDatabase = withoutScheme[(atIndex + 1)..];
            var colonIndex = userInfo.IndexOf(':');
            if (colonIndex <= 0 || colonIndex >= userInfo.Length - 1)
            {
                throw new InvalidOperationException("Dashboard database URL must include username and password.");
            }

            var username = Uri.UnescapeDataString(userInfo[..colonIndex]);
            var password = Uri.UnescapeDataString(userInfo[(colonIndex + 1)..]);
            var slashIndex = hostAndDatabase.IndexOf('/');
            if (slashIndex <= 0 || slashIndex >= hostAndDatabase.Length - 1)
            {
                throw new InvalidOperationException("Dashboard database URL must include database name.");
            }

            var hostPort = hostAndDatabase[..slashIndex];
            var databaseName = hostAndDatabase[(slashIndex + 1)..].Split('?', 2)[0];
            var host = hostPort;
            var port = 5432;

            var lastColonIndex = hostPort.LastIndexOf(':');
            if (lastColonIndex > 0 && int.TryParse(hostPort[(lastColonIndex + 1)..], out var parsedPort))
            {
                host = hostPort[..lastColonIndex];
                port = parsedPort;
            }

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Database = Uri.UnescapeDataString(databaseName),
                Username = username,
                Password = password,
                SslMode = SslMode.Require
            };

            ApplyRecommendedDefaults(builder);
            return builder.ConnectionString;
        }

        throw new InvalidOperationException("Dashboard database URL must be a valid PostgreSQL URI.");
    }

    private static void ApplyRecommendedDefaults(NpgsqlConnectionStringBuilder builder)
    {
        if (builder.Timeout <= 0)
        {
            builder.Timeout = 15;
        }

        if (builder.CommandTimeout <= 0)
        {
            builder.CommandTimeout = 60;
        }

        if (builder.KeepAlive <= 0)
        {
            builder.KeepAlive = 30;
        }

        builder.Pooling = true;
    }
}

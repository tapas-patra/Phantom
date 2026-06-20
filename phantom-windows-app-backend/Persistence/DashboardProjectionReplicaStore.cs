using Npgsql;
using NpgsqlTypes;
using Phantom.WindowsApp.Backend.Infrastructure;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class DashboardProjectionReplicaStore
{
    private readonly string _connectionString;
    private readonly bool _enabled;

    public DashboardProjectionReplicaStore(BackendOptions options)
    {
        _enabled = options.HasDashboardProjectionReplica;
        _connectionString = _enabled
            ? BuildConnectionString(options.DashboardProjectionDatabaseUrl)
            : string.Empty;

        if (_enabled)
        {
            using var connection = OpenConnection();
            SchemaMigrator.ApplyMigrations(connection, BackendSchemaMigrations.DashboardProjectionOnly);
        }
    }

    public bool IsEnabled => _enabled;

    public NpgsqlConnection OpenConnection()
    {
        if (!_enabled)
        {
            throw new InvalidOperationException("Dashboard projection replica is not configured.");
        }

        var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
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
                throw new InvalidOperationException("Dashboard projection database URL must include credentials and host.");
            }

            var userInfo = withoutScheme[..atIndex];
            var hostAndDatabase = withoutScheme[(atIndex + 1)..];
            var colonIndex = userInfo.IndexOf(':');
            if (colonIndex <= 0 || colonIndex >= userInfo.Length - 1)
            {
                throw new InvalidOperationException("Dashboard projection database URL must include username and password.");
            }

            var username = Uri.UnescapeDataString(userInfo[..colonIndex]);
            var password = Uri.UnescapeDataString(userInfo[(colonIndex + 1)..]);
            var slashIndex = hostAndDatabase.IndexOf('/');
            if (slashIndex <= 0 || slashIndex >= hostAndDatabase.Length - 1)
            {
                throw new InvalidOperationException("Dashboard projection database URL must include database name.");
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

        throw new InvalidOperationException("Dashboard projection database URL must be a valid PostgreSQL URI.");
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

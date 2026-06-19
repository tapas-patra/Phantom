using Npgsql;
using Phantom.WindowsApp.Backend.Infrastructure;
using System.Net.Sockets;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class PostgresBackendStore
{
    private readonly string _connectionString;

    public PostgresBackendStore(BackendOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DatabaseUrl))
        {
            throw new InvalidOperationException(
                "Backend database URL is not configured. Set PHANTOM_WINDOWS_BACKEND_DATABASE_URL.");
        }

        _connectionString = BuildConnectionString(options.DatabaseUrl);
        using var connection = OpenConnection();
        SchemaMigrator.ApplyMigrations(connection, BackendSchemaMigrations.All);
    }

    public NpgsqlConnection OpenConnection()
    {
        try
        {
            var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            return connection;
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                "Could not resolve the PostgreSQL host from PHANTOM_WINDOWS_BACKEND_DATABASE_URL. " +
                "Check the Host value in local-dev.env.ps1 or your shell environment.",
                ex);
        }
        catch (NpgsqlException ex) when (ex.InnerException is SocketException)
        {
            throw new InvalidOperationException(
                "Could not connect to PostgreSQL using PHANTOM_WINDOWS_BACKEND_DATABASE_URL. " +
                "Check the hostname, port, and network reachability for the configured database.",
                ex);
        }
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
            return BuildConnectionStringFromUriLikeValue(databaseUrl);
        }

        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("PHANTOM_WINDOWS_BACKEND_DATABASE_URL must be a valid PostgreSQL URI.");
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.Trim('/'),
            Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty,
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            SslMode = SslMode.Require
        };

        ApplyRecommendedDefaults(builder);
        return builder.ConnectionString;
    }

    private static string BuildConnectionStringFromUriLikeValue(string databaseUrl)
    {
        var withoutScheme = databaseUrl
            .Replace("postgresql://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("postgres://", string.Empty, StringComparison.OrdinalIgnoreCase);

        var atIndex = withoutScheme.LastIndexOf('@');
        if (atIndex <= 0 || atIndex >= withoutScheme.Length - 1)
        {
            throw new InvalidOperationException("PHANTOM_WINDOWS_BACKEND_DATABASE_URL must include credentials and host.");
        }

        var userInfo = withoutScheme[..atIndex];
        var hostAndDatabase = withoutScheme[(atIndex + 1)..];

        var colonIndex = userInfo.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= userInfo.Length - 1)
        {
            throw new InvalidOperationException("PHANTOM_WINDOWS_BACKEND_DATABASE_URL must include username and password.");
        }

        var username = Uri.UnescapeDataString(userInfo[..colonIndex]);
        var password = Uri.UnescapeDataString(userInfo[(colonIndex + 1)..]);

        var slashIndex = hostAndDatabase.IndexOf('/');
        if (slashIndex <= 0 || slashIndex >= hostAndDatabase.Length - 1)
        {
            throw new InvalidOperationException("PHANTOM_WINDOWS_BACKEND_DATABASE_URL must include database name.");
        }

        var hostPort = hostAndDatabase[..slashIndex];
        var databaseAndQuery = hostAndDatabase[(slashIndex + 1)..];
        var queryIndex = databaseAndQuery.IndexOf('?');
        var databaseName = queryIndex >= 0
            ? databaseAndQuery[..queryIndex]
            : databaseAndQuery;

        var host = hostPort;
        var port = 5432;
        var lastColonIndex = hostPort.LastIndexOf(':');
        if (lastColonIndex > 0 && lastColonIndex < hostPort.Length - 1
            && int.TryParse(hostPort[(lastColonIndex + 1)..], out var parsedPort))
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

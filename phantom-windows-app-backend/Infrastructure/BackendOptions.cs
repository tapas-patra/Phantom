using Microsoft.Extensions.Configuration;

namespace Phantom.WindowsApp.Backend.Infrastructure;

public sealed class BackendOptions
{
    public string DatabasePath { get; init; } = "data/phantom-windows-app-backend.db";
    public int DefaultLeaseHours { get; init; } = 24;
    public int LockTtlMinutes { get; init; } = 5;
    public decimal DefaultProCredits { get; init; } = 1.0m;
    public decimal DefaultPremiumCredits { get; init; } = 0m;

    public static BackendOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("PhantomBackend");
        return new BackendOptions
        {
            DatabasePath = Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_DB_PATH")
                ?? section["DatabasePath"]
                ?? "data/phantom-windows-app-backend.db",
            DefaultLeaseHours = ParseInt(Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_LEASE_HOURS"), section["DefaultLeaseHours"], 24),
            LockTtlMinutes = ParseInt(Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_LOCK_TTL_MINUTES"), section["LockTtlMinutes"], 5),
            DefaultProCredits = ParseDecimal(Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_DEFAULT_PRO_CREDITS"), section["DefaultProCredits"], 1.0m),
            DefaultPremiumCredits = ParseDecimal(Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_DEFAULT_PREMIUM_CREDITS"), section["DefaultPremiumCredits"], 0m)
        };
    }

    private static int ParseInt(string? envValue, string? configValue, int fallback)
    {
        if (int.TryParse(envValue, out var envParsed))
        {
            return envParsed;
        }

        if (int.TryParse(configValue, out var configParsed))
        {
            return configParsed;
        }

        return fallback;
    }

    private static decimal ParseDecimal(string? envValue, string? configValue, decimal fallback)
    {
        if (decimal.TryParse(envValue, out var envParsed))
        {
            return envParsed;
        }

        if (decimal.TryParse(configValue, out var configParsed))
        {
            return configParsed;
        }

        return fallback;
    }
}

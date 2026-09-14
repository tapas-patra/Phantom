using System.Text.Json;
using Phantom.Dashboard.Backend.Infrastructure;

namespace Phantom.Dashboard.Backend.Services;

public sealed class DesktopReleaseVersionClient
{
    public const string FallbackVersion = "Latest automated build";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly DashboardOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DesktopReleaseVersionClient> _logger;
    private readonly object _gate = new();
    private string? _cached;
    private DateTime _cachedAtUtc;

    public DesktopReleaseVersionClient(
        DashboardOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<DesktopReleaseVersionClient> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string GetInstallerVersion()
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(_cached) && DateTime.UtcNow - _cachedAtUtc < CacheTtl)
                return _cached;
        }

        try
        {
            var url =
                $"https://github.com/{_options.ReleaseRepository}/releases/download/{_options.ReleaseTag}/latest.json";
            var client = _httpClientFactory.CreateClient(nameof(DesktopReleaseVersionClient));
            using var response = client.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var payload = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var parsed = JsonSerializer.Deserialize<LatestReleaseFile>(payload, JsonOptions);
            var version = parsed?.Version?.Trim();
            if (!string.IsNullOrWhiteSpace(version))
            {
                lock (_gate)
                {
                    _cached = version;
                    _cachedAtUtc = DateTime.UtcNow;
                }
                return version;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read desktop latest.json from GitHub Releases.");
        }

        lock (_gate)
        {
            return string.IsNullOrWhiteSpace(_cached) ? FallbackVersion : _cached;
        }
    }

    private sealed class LatestReleaseFile
    {
        public string? Version { get; set; }
    }
}

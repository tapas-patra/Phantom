namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class ManagedAiLatencyStatusDto
{
    public ManagedAiLatencyRunDto? LatestRun { get; set; }
    public IReadOnlyList<ManagedAiLatencyModelStatusDto> Models { get; set; } = Array.Empty<ManagedAiLatencyModelStatusDto>();
}

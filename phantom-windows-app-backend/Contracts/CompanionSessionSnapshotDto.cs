namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class CompanionSessionSnapshotDto
{
    public string PairingId { get; set; } = string.Empty;
    public string DesktopStatus { get; set; } = "ready";
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool Vision { get; set; }
    public IReadOnlyList<CompanionDisplayDto> Displays { get; set; } = Array.Empty<CompanionDisplayDto>();
    public string SelectedDisplayId { get; set; } = string.Empty;
    public IReadOnlyList<CompanionTurnDto> Turns { get; set; } = Array.Empty<CompanionTurnDto>();
}

public sealed class CompanionDisplayDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public sealed class CompanionTurnDto
{
    public string Role { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime AtUtc { get; set; }
}

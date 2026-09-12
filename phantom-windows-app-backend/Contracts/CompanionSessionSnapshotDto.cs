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
    public int AttachmentCount { get; set; }
    public IReadOnlyList<CompanionAttachmentDto> Attachments { get; set; } = Array.Empty<CompanionAttachmentDto>();
    public IReadOnlyList<CompanionProviderOptionDto> Providers { get; set; } = Array.Empty<CompanionProviderOptionDto>();
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

public sealed class CompanionProviderOptionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<CompanionModelOptionDto> Models { get; set; } = Array.Empty<CompanionModelOptionDto>();
}

public sealed class CompanionModelOptionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Vision { get; set; }
}

public sealed class CompanionAttachmentDto
{
    public int Index { get; set; }
    public string? ThumbnailJpegBase64 { get; set; }
}

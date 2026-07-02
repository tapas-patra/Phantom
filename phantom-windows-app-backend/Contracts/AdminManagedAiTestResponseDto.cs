namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminManagedAiTestResponseDto
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OutputText { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public int LatencyMs { get; set; }
    public bool? IsChatCapable { get; set; }
    public DateTime TestedAtUtc { get; set; }
}

namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class ReadOnlySafeModeStateDto
{
    public bool IsEnabled { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool CanUploadDiagnostics { get; set; }
}

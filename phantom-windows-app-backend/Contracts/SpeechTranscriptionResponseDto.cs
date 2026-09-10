namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class SpeechTranscriptionResponseDto
{
    public string Text { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
}

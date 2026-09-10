namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class DesktopAiChatRequestDto
{
    public const int MaxAttachedImages = 3;

    public string RequestId { get; set; } = string.Empty;
    public string TurnId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool AllowPaidSessionExtension { get; set; }

    /// <summary>Legacy single-image field. Prefer <see cref="ImagesBase64"/>.</summary>
    public string? ImageBase64 { get; set; }

    public IReadOnlyList<string>? ImagesBase64 { get; set; }

    public IReadOnlyList<DesktopAiChatMessageDto> Messages { get; set; } = Array.Empty<DesktopAiChatMessageDto>();

    public IReadOnlyList<string> GetNormalizedImages()
    {
        var images = new List<string>(MaxAttachedImages);
        if (ImagesBase64 != null)
        {
            foreach (var image in ImagesBase64)
            {
                if (string.IsNullOrWhiteSpace(image))
                {
                    continue;
                }

                images.Add(image.Trim());
                if (images.Count >= MaxAttachedImages)
                {
                    return images;
                }
            }
        }

        if (images.Count == 0 && !string.IsNullOrWhiteSpace(ImageBase64))
        {
            images.Add(ImageBase64.Trim());
        }

        return images;
    }
}

public sealed class DesktopAiChatMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

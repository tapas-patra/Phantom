using System;
using System.Collections.Generic;

namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class CompanionPairingStartRequestDto
    {
        public string DesktopDeviceLabel { get; set; } = string.Empty;
        public string DesktopPlatform { get; set; } = "windows";
        public string AppVersion { get; set; } = string.Empty;
    }

    public sealed class CompanionPairingStartResultDto
    {
        public string Code { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
        public string QrPayload { get; set; } = string.Empty;
    }

    public sealed class CompanionPairingCompleteRequestDto
    {
        public string Code { get; set; } = string.Empty;
        public string CompanionDeviceId { get; set; } = string.Empty;
        public string CompanionDeviceLabel { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
    }

    public sealed class CompanionPairingCompleteResultDto
    {
        public string PairingId { get; set; } = string.Empty;
        public string DesktopDeviceLabel { get; set; } = string.Empty;
        public string DesktopPlatform { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public bool RelayRequired { get; set; }
    }

    public sealed class CompanionPairingDto
    {
        public string PairingId { get; set; } = string.Empty;
        public string DesktopDeviceLabel { get; set; } = string.Empty;
        public string DesktopPlatform { get; set; } = string.Empty;
        public string CompanionDeviceLabel { get; set; } = string.Empty;
        public string CompanionPlatform { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public bool DesktopOnline { get; set; }
        public bool PhoneOnline { get; set; }
    }

    public sealed class CompanionPairingsResponseDto
    {
        public List<CompanionPairingDto> Pairings { get; set; } = new();
    }

    public sealed class CompanionRevokeResultDto
    {
        public bool Revoked { get; set; }
    }

    public sealed class CompanionRelayTicketRequestDto
    {
        public string PairingId { get; set; } = string.Empty;
        public string Role { get; set; } = "desktop";
    }

    public sealed class CompanionRelayTicketDto
    {
        public string Ticket { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
        public string RelayUrl { get; set; } = string.Empty;
    }

    public sealed class CompanionSessionSnapshotDto
    {
        public string PairingId { get; set; } = string.Empty;
        public string DesktopStatus { get; set; } = "ready";
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public bool Vision { get; set; }
        public List<CompanionDisplayDto> Displays { get; set; } = new();
        public string SelectedDisplayId { get; set; } = string.Empty;
        public List<CompanionTurnDto> Turns { get; set; } = new();
        public int AttachmentCount { get; set; }
        public List<CompanionAttachmentDto> Attachments { get; set; } = new();
        public List<CompanionProviderOptionDto> Providers { get; set; } = new();
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
        public List<CompanionModelOptionDto> Models { get; set; } = new();
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
}

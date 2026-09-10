using System.Security.Cryptography;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class CompanionPairingService
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(10);

    private readonly BackendOptions _options;
    private readonly CompanionPairingRepository _pairings;
    private readonly CompanionPairingCodeRepository _codes;
    private readonly CompanionAuditRepository _audit;
    private readonly CompanionRelayHost _relay;
    private readonly TokenService _tokens;

    public CompanionPairingService(
        BackendOptions options,
        CompanionPairingRepository pairings,
        CompanionPairingCodeRepository codes,
        CompanionAuditRepository audit,
        CompanionRelayHost relay,
        TokenService tokens)
    {
        _options = options;
        _pairings = pairings;
        _codes = codes;
        _audit = audit;
        _relay = relay;
        _tokens = tokens;
    }

    public CompanionPairingStartResultDto Start(DesktopSessionRecord session, CompanionPairingStartRequestDto request)
    {
        var platform = NormalizePlatform(request.DesktopPlatform);
        var deviceId = session.DeviceInstallId;

        // Reject if this device is already registered as a companion on any active pairing.
        var existingCompanion = _pairings.FindActiveByCompanionDevice(deviceId);
        if (existingCompanion != null)
        {
            throw new BackendValidationException("This device is already registered as a companion.");
        }

        // Starting a new code invalidates any unused code for that desktop device.
        _codes.DeleteUnusedForDevice(deviceId);

        var code = GenerateCode();
        var codeHash = _tokens.HashToken(code);
        var expiresAt = DateTime.UtcNow.Add(CodeTtl);
        _codes.Save(new CompanionPairingCodeRecord
        {
            CodeHash = codeHash,
            UserId = session.UserId,
            DesktopDeviceId = deviceId,
            DesktopDeviceLabel = request.DesktopDeviceLabel ?? string.Empty,
            DesktopPlatform = platform,
            AppVersion = request.AppVersion ?? string.Empty,
            ExpiresAtUtc = expiresAt,
            ConsumedAtUtc = null
        });

        _audit.Record(session.UserId, string.Empty, "pairing_started", "desktop");

        var relayBase = ResolvePublicApiBaseUrl();
        var qrPayload = $"phantom-companion://pair?code={code}&relay={relayBase}";

        return new CompanionPairingStartResultDto
        {
            Code = code,
            ExpiresAtUtc = expiresAt,
            QrPayload = qrPayload
        };
    }

    public CompanionPairingCompleteResultDto Complete(DesktopSessionRecord session, CompanionPairingCompleteRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new BackendValidationException("Pairing code is invalid or expired.");
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var codeHash = _tokens.HashToken(normalizedCode);
        var code = _codes.FindByHash(codeHash);
        if (code == null || code.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new BackendValidationException("Pairing code is invalid or expired.");
        }

        if (code.ConsumedAtUtc.HasValue)
        {
            throw new BackendValidationException("Pairing code already used.");
        }

        if (!string.Equals(code.UserId, session.UserId, StringComparison.Ordinal))
        {
            throw new BackendValidationException("Pairing code is invalid or expired.");
        }

        if (string.IsNullOrWhiteSpace(request.CompanionDeviceId))
        {
            throw new BackendValidationException("Companion device id is required.");
        }

        // v1: one active pairing per desktop device.
        var active = _pairings.FindActiveByDesktopDevice(code.DesktopDeviceId);
        if (active != null)
        {
            throw new BackendValidationException("This desktop already has an active companion. Revoke it first.", "pairing_already_active");
        }

        var now = DateTime.UtcNow;
        var pairingId = $"pair-{Guid.NewGuid():N}";
        var record = new CompanionPairingRecord
        {
            PairingId = pairingId,
            UserId = session.UserId,
            DesktopDeviceId = code.DesktopDeviceId,
            DesktopDeviceLabel = code.DesktopDeviceLabel,
            DesktopPlatform = code.DesktopPlatform,
            CompanionDeviceId = request.CompanionDeviceId,
            CompanionDeviceLabel = request.CompanionDeviceLabel ?? string.Empty,
            CompanionPlatform = NormalizePlatform(request.Platform),
            Status = "active",
            CreatedAtUtc = now,
            PairedAtUtc = now,
            RevokedAtUtc = null
        };
        _pairings.Save(record);
        _codes.MarkConsumed(codeHash, now);
        _audit.Record(session.UserId, pairingId, "pairing_completed", "phone");

        return new CompanionPairingCompleteResultDto
        {
            PairingId = pairingId,
            DesktopDeviceLabel = record.DesktopDeviceLabel,
            DesktopPlatform = record.DesktopPlatform,
            CreatedAtUtc = now,
            RelayRequired = true
        };
    }

    public CompanionPairingsResponseDto List(DesktopSessionRecord session)
    {
        var records = _pairings.ListByUser(session.UserId);
        var dtos = new List<CompanionPairingDto>();
        foreach (var record in records)
        {
            if (record.RevokedAtUtc.HasValue)
            {
                continue;
            }
            dtos.Add(new CompanionPairingDto
            {
                PairingId = record.PairingId,
                DesktopDeviceLabel = record.DesktopDeviceLabel,
                DesktopPlatform = record.DesktopPlatform,
                CompanionDeviceLabel = record.CompanionDeviceLabel,
                CompanionPlatform = record.CompanionPlatform,
                CreatedAtUtc = record.CreatedAtUtc,
                DesktopOnline = _relay.IsDesktopOnline(record.PairingId),
                PhoneOnline = _relay.IsPhoneOnline(record.PairingId)
            });
        }
        return new CompanionPairingsResponseDto { Pairings = dtos };
    }

    public CompanionSessionSnapshotDto? GetCurrentSnapshot(string pairingId)
    {
        return _relay.GetSnapshot(pairingId);
    }

    public object Revoke(DesktopSessionRecord session, string pairingId)
    {
        var record = _pairings.FindById(pairingId)
            ?? throw new BackendValidationException("Pairing not found.");

        if (!string.Equals(record.UserId, session.UserId, StringComparison.Ordinal))
        {
            throw new BackendValidationException("Pairing not found.");
        }

        if (record.RevokedAtUtc.HasValue)
        {
            return new { revoked = true };
        }

        _pairings.Revoke(pairingId);
        _relay.DropRoom(pairingId, "pairing_revoked");
        _audit.Record(session.UserId, pairingId, "pairing_revoked", string.Empty);
        return new { revoked = true };
    }

    private string ResolvePublicApiBaseUrl()
    {
        if (Uri.TryCreate(_options.PublicApiBaseUrl, UriKind.Absolute, out var configured) && !string.IsNullOrWhiteSpace(_options.PublicApiBaseUrl))
        {
            return configured.ToString().TrimEnd('/');
        }
        return "https://phantom-ai-windows-app-backend.onrender.com";
    }

    private static string NormalizePlatform(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return string.Empty;
        }
        var lower = platform.Trim().ToLowerInvariant();
        return lower switch
        {
            "windows" or "macos" or "android" or "ios" => lower,
            _ => lower
        };
    }

    private static string GenerateCode()
    {
        var buffer = new char[6];
        var bytes = RandomNumberGenerator.GetBytes(6);
        for (var i = 0; i < 6; i++)
        {
            buffer[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
        }
        return new string(buffer);
    }
}

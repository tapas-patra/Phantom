using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Npgsql;
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
    private readonly PostgresBackendStore _store;
    private readonly CompanionPairingRepository _pairings;
    private readonly CompanionPairingCodeRepository _codes;
    private readonly CompanionAuditRepository _audit;
    private readonly CompanionRelayHost _relay;
    private readonly TokenService _tokens;

    public CompanionPairingService(
        BackendOptions options,
        PostgresBackendStore store,
        CompanionPairingRepository pairings,
        CompanionPairingCodeRepository codes,
        CompanionAuditRepository audit,
        CompanionRelayHost relay,
        TokenService tokens)
    {
        _options = options;
        _store = store;
        _pairings = pairings;
        _codes = codes;
        _audit = audit;
        _relay = relay;
        _tokens = tokens;
    }

    /// <summary>
    /// True when <paramref name="deviceId"/> is registered as the companion (phone)
    /// device on any active (non-revoked) pairing. Used to gate desktop-only routes
    /// (locks, managed AI chat, pairings/start) so a paired phone session cannot
    /// bypass the relay. See spec §4.7.
    /// </summary>
    public bool IsCompanionDevice(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;
        return _pairings.FindActiveByCompanionDevice(deviceId) != null;
    }

    public CompanionPairingStartResultDto Start(DesktopSessionRecord session, CompanionPairingStartRequestDto request, HttpContext? httpContext = null)
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

        var relayBase = ResolvePublicApiBaseUrl(httpContext);
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

        // Bind the companion device to the calling session's device id, not the request
        // body. The phone authenticates with its own install id, so session.DeviceInstallId
        // is the authoritative companion device id (spec §4.5 admission). Ignoring the
        // body field prevents intentional mis-binding.
        var companionDeviceId = session.DeviceInstallId;
        if (string.IsNullOrWhiteSpace(companionDeviceId))
        {
            throw new BackendValidationException("Companion device id is required.");
        }

        // The desktop that issued the code cannot complete it (caller must be the phone).
        // This also prevents a desktop from binding itself as its own companion.
        // Transactional: lock the code row, re-validate, then insert the pairing and mark
        // the code consumed atomically. Closes the double-redemption and active-pairing
        // races (spec §4.2, §6.1 unique index).
        using var connection = _store.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var code = _codes.FindByHash(codeHash, connection, transaction, forUpdate: true);
            if (code == null || code.ExpiresAtUtc <= DateTime.UtcNow)
            {
                transaction.Rollback();
                throw new BackendValidationException("Pairing code is invalid or expired.");
            }
            if (code.ConsumedAtUtc.HasValue)
            {
                transaction.Rollback();
                throw new BackendValidationException("Pairing code already used.");
            }
            if (!string.Equals(code.UserId, session.UserId, StringComparison.Ordinal))
            {
                transaction.Rollback();
                throw new BackendValidationException("Pairing code is invalid or expired.");
            }
            if (string.Equals(companionDeviceId, code.DesktopDeviceId, StringComparison.Ordinal))
            {
                transaction.Rollback();
                throw new BackendValidationException("Pairing code is invalid or expired.");
            }

            // v1: one active pairing per desktop device. FOR UPDATE serializes concurrent
            // completes for the same desktop; the partial unique index is the backstop.
            var active = _pairings.FindActiveByDesktopDevice(code.DesktopDeviceId, connection, transaction, forUpdate: true);
            if (active != null)
            {
                transaction.Rollback();
                throw new BackendConflictException("This desktop already has an active companion. Revoke it first.", "pairing_already_active");
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
                CompanionDeviceId = companionDeviceId,
                CompanionDeviceLabel = request.CompanionDeviceLabel ?? string.Empty,
                CompanionPlatform = NormalizePlatform(request.Platform),
                Status = "active",
                CreatedAtUtc = now,
                PairedAtUtc = now,
                RevokedAtUtc = null
            };

            try
            {
                _pairings.Save(record, connection, transaction);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                transaction.Rollback();
                throw new BackendConflictException("This desktop already has an active companion. Revoke it first.", "pairing_already_active");
            }

            _codes.MarkConsumed(codeHash, now, connection, transaction);
            transaction.Commit();

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
        catch (BackendValidationException)
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
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

    /// <summary>
    /// Returns the active pairing for the calling device (either as the desktop or the
    /// companion), so <c>GET /sessions/current</c> resolves the right room for accounts
    /// with multiple desktops (spec §4.6).
    /// </summary>
    public CompanionSessionSnapshotDto? GetCurrentSnapshotForDevice(DesktopSessionRecord session)
    {
        var pairingId = ResolveActivePairingIdForDevice(session.DeviceInstallId);
        if (string.IsNullOrEmpty(pairingId)) return null;
        return _relay.GetSnapshot(pairingId);
    }

    public CompanionSessionSnapshotDto? GetCurrentSnapshot(string pairingId)
    {
        return _relay.GetSnapshot(pairingId);
    }

    private string? ResolveActivePairingIdForDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;
        var asDesktop = _pairings.FindActiveByDesktopDevice(deviceId);
        if (asDesktop != null) return asDesktop.PairingId;
        var asCompanion = _pairings.FindActiveByCompanionDevice(deviceId);
        return asCompanion?.PairingId;
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

    /// <summary>
    /// Resolves the public API base used to build QR payloads and relay URLs.
    /// Fallback chain (spec §6.3): configured env/config → request https://{Host} →
    /// hard last resort (Render production URL).
    /// </summary>
    internal string ResolvePublicApiBaseUrl(HttpContext? httpContext)
    {
        if (Uri.TryCreate(_options.PublicApiBaseUrl, UriKind.Absolute, out var configured)
            && !string.IsNullOrWhiteSpace(_options.PublicApiBaseUrl))
        {
            return configured.ToString().TrimEnd('/');
        }

        if (httpContext != null)
        {
            var host = httpContext.Request.Host.Host;
            if (!string.IsNullOrWhiteSpace(host))
            {
                var scheme = httpContext.Request.IsHttps || httpContext.Request.Headers.TryGetValue("X-Forwarded-Proto", out var forwarded)
                    ? "https"
                    : httpContext.Request.Scheme;
                return $"{scheme}://{host}";
            }
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

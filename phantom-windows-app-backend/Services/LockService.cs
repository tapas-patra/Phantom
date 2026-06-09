using System.Security.Cryptography;
using System.Text;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class LockService
{
    private readonly BackendOptions _options;
    private readonly LockRepository _locks;

    public LockService(BackendOptions options, LockRepository locks)
    {
        _options = options;
        _locks = locks;
    }

    public DeviceLockAcquireResultDto Acquire(DeviceLockAcquireRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new BackendValidationException("UserId, DeviceId, and SessionId are required.");
        }

        var existing = _locks.FindActiveByUser(request.UserId);
        if (existing != null && existing.ExpiresAtUtc > DateTime.UtcNow
            && (!string.Equals(existing.DeviceId, request.DeviceId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(existing.SessionId, request.SessionId, StringComparison.OrdinalIgnoreCase)))
        {
            return new DeviceLockAcquireResultDto
            {
                Acquired = false,
                LockToken = string.Empty,
                ExpiresAtUtc = existing.ExpiresAtUtc,
                HolderDeviceId = existing.DeviceId,
                HolderSessionId = existing.SessionId
            };
        }

        var record = new DesktopLockRecord
        {
            SessionId = request.SessionId,
            UserId = request.UserId,
            DeviceId = request.DeviceId,
            LockToken = Hash($"{request.UserId}:{request.DeviceId}:{Guid.NewGuid():N}"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(_options.LockTtlMinutes),
            LastHeartbeatAtUtc = DateTime.UtcNow,
            AppVersion = request.AppVersion
        };
        _locks.Save(record);

        return new DeviceLockAcquireResultDto
        {
            Acquired = true,
            LockToken = record.LockToken,
            ExpiresAtUtc = record.ExpiresAtUtc,
            HolderDeviceId = record.DeviceId,
            HolderSessionId = record.SessionId
        };
    }

    public object Heartbeat(DeviceLockHeartbeatRequestDto request)
    {
        var record = _locks.FindBySessionId(request.SessionId)
            ?? throw new BackendValidationException("Active lock not found.");

        if (!string.Equals(record.LockToken, request.LockToken, StringComparison.Ordinal)
            || !string.Equals(record.DeviceId, request.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new BackendValidationException("Lock token mismatch.");
        }

        record.LastHeartbeatAtUtc = DateTime.UtcNow;
        record.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(_options.LockTtlMinutes);
        _locks.Save(record);

        return new
        {
            succeeded = true,
            expiresAtUtc = record.ExpiresAtUtc
        };
    }

    public object Release(DeviceLockReleaseRequestDto request)
    {
        var record = _locks.FindBySessionId(request.SessionId)
            ?? throw new BackendValidationException("Active lock not found.");

        if (!string.Equals(record.LockToken, request.LockToken, StringComparison.Ordinal))
        {
            throw new BackendValidationException("Lock token mismatch.");
        }

        _locks.Delete(request.SessionId);
        return new
        {
            released = true,
            reason = request.ReleaseReason
        };
    }

    private static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}

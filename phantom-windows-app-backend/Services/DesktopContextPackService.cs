using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class DesktopContextPackService
{
    private readonly DesktopContextPackRepository _packs;
    private readonly AuthSessionRepository _sessions;
    private readonly AccountRepository _accounts;
    private readonly TokenService _tokens;

    public DesktopContextPackService(
        DesktopContextPackRepository packs,
        AuthSessionRepository sessions,
        AccountRepository accounts,
        TokenService tokens)
    {
        _packs = packs;
        _sessions = sessions;
        _accounts = accounts;
        _tokens = tokens;
    }

    public DesktopAccountRecord RequirePremiumAccountFromAccessToken(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new BackendValidationException("Authorization bearer token is required.");
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new BackendValidationException("Authorization bearer token is required.");
        }

        var session = _sessions.FindByAccessTokenHash(_tokens.HashToken(token))
            ?? throw new BackendValidationException("Desktop session not found.");

        if (!session.IsAuthenticated || session.RevokedAtUtc.HasValue || session.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new BackendValidationException("Desktop session is no longer valid.");
        }

        var account = _accounts.FindByUserId(session.UserId)
            ?? throw new BackendValidationException("Account not found.");

        if (!string.Equals(account.AccessTier, "premium", StringComparison.OrdinalIgnoreCase))
        {
            throw new BackendValidationException("Context Packs are available only for Premium accounts.");
        }

        return account;
    }

    public IReadOnlyList<DesktopContextPackDto> List(DesktopAccountRecord account)
    {
        return _packs.ListByUserId(account.UserId)
            .Select(Map)
            .ToArray();
    }

    public DesktopContextPackDto Upsert(DesktopAccountRecord account, DesktopContextPackUpsertRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new BackendValidationException("Context pack name is required.");
        }

        var now = DateTime.UtcNow;
        var existing = !string.IsNullOrWhiteSpace(request.PackId)
            ? _packs.FindById(request.PackId.Trim())
            : null;
        if (existing != null && !string.Equals(existing.UserId, account.UserId, StringComparison.Ordinal))
        {
            throw new BackendValidationException("Context pack not found.");
        }

        var record = existing ?? new DesktopContextPackRecord
        {
            PackId = string.IsNullOrWhiteSpace(request.PackId)
                ? $"ctx-{Guid.NewGuid():N}"
                : request.PackId.Trim(),
            UserId = account.UserId,
            CreatedAtUtc = now
        };

        var trimmedName = request.Name.Trim();
        var trimmedResumeText = request.ResumeText?.Trim() ?? string.Empty;
        var trimmedJobDescriptionText = request.JobDescriptionText?.Trim() ?? string.Empty;

        if (existing != null
            && string.Equals(existing.Name, trimmedName, StringComparison.Ordinal)
            && string.Equals(existing.ResumeText, trimmedResumeText, StringComparison.Ordinal)
            && string.Equals(existing.JobDescriptionText, trimmedJobDescriptionText, StringComparison.Ordinal))
        {
            return Map(existing);
        }

        record.Name = trimmedName;
        record.ResumeText = trimmedResumeText;
        record.JobDescriptionText = trimmedJobDescriptionText;
        record.UpdatedAtUtc = now;

        _packs.Save(record);
        return Map(record);
    }

    public void Delete(DesktopAccountRecord account, string packId)
    {
        if (string.IsNullOrWhiteSpace(packId))
        {
            throw new BackendValidationException("Context pack id is required.");
        }

        var existing = _packs.FindById(packId.Trim());
        if (existing == null || !string.Equals(existing.UserId, account.UserId, StringComparison.Ordinal))
        {
            throw new BackendValidationException("Context pack not found.");
        }

        _packs.Delete(existing.PackId);
    }

    private static DesktopContextPackDto Map(DesktopContextPackRecord record)
    {
        return new DesktopContextPackDto
        {
            PackId = record.PackId,
            Name = record.Name,
            ResumeText = record.ResumeText,
            JobDescriptionText = record.JobDescriptionText,
            UpdatedAtUtc = record.UpdatedAtUtc
        };
    }
}

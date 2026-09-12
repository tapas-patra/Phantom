using Microsoft.AspNetCore.Http;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class CompanionRelayTicketService
{
    private static readonly TimeSpan TicketTtl = TimeSpan.FromMinutes(5);

    private readonly BackendOptions _options;
    private readonly CompanionPairingRepository _pairings;
    private readonly CompanionRelayTicketRepository _tickets;
    private readonly TokenService _tokens;

    public CompanionRelayTicketService(
        BackendOptions options,
        CompanionPairingRepository pairings,
        CompanionRelayTicketRepository tickets,
        TokenService tokens)
    {
        _options = options;
        _pairings = pairings;
        _tickets = tickets;
        _tokens = tokens;
    }

    public CompanionRelayTicketDto Issue(DesktopSessionRecord session, CompanionRelayTicketRequestDto request, HttpContext? httpContext = null)
    {
        var role = NormalizeRole(request.Role);
        var pairing = _pairings.FindById(request.PairingId)
            ?? throw new BackendValidationException("Pairing not found.");

        if (!string.Equals(pairing.UserId, session.UserId, StringComparison.Ordinal))
        {
            throw new BackendValidationException("Pairing not found.");
        }
        if (pairing.RevokedAtUtc.HasValue)
        {
            throw new BackendValidationException("Pairing has been revoked.");
        }

        var expectedDeviceId = role switch
        {
            "desktop" => pairing.DesktopDeviceId,
            "phone" => pairing.CompanionDeviceId,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(expectedDeviceId))
        {
            throw new BackendValidationException("Pairing is not complete.");
        }

        if (!string.Equals(expectedDeviceId, session.DeviceInstallId, StringComparison.Ordinal))
        {
            throw new BackendValidationException("This device is not authorized for this pairing role.");
        }

        var ticket = _tokens.GenerateOpaqueToken();
        var ticketHash = _tokens.HashToken(ticket);
        var expiresAt = DateTime.UtcNow.Add(TicketTtl);
        _tickets.Save(new CompanionRelayTicketRecord
        {
            TicketHash = ticketHash,
            UserId = session.UserId,
            PairingId = pairing.PairingId,
            Role = role,
            DeviceId = session.DeviceInstallId,
            ExpiresAtUtc = expiresAt,
            ConsumedAtUtc = null
        });

        return new CompanionRelayTicketDto
        {
            Ticket = ticket,
            ExpiresAtUtc = expiresAt,
            RelayUrl = ResolveRelayUrl(httpContext)
        };
    }

    /// <summary>
    /// Validates and atomically consumes a relay ticket presented over the WebSocket
    /// upgrade query string. The conditional UPDATE (consumed_at_utc IS NULL) is the
    /// single source of truth for single-use admission: if it affects zero rows the
    /// ticket was already consumed by a concurrent upgrade (spec §4.5 / §5).
    /// </summary>
    public CompanionRelayTicketRecord Consume(string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            throw new BackendValidationException("Relay ticket is required.", "ticket_missing");
        }

        var ticketHash = _tokens.HashToken(ticket.Trim());
        var record = _tickets.FindByHash(ticketHash)
            ?? throw new BackendValidationException("Relay ticket is invalid.", "ticket_invalid");

        if (record.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new BackendValidationException("Relay ticket has expired.", "ticket_expired");
        }

        // Atomic single-use consume. A zero row count means another upgrade already
        // consumed it (or it was consumed between our read and update) — reject.
        var affected = _tickets.MarkConsumed(ticketHash, DateTime.UtcNow);
        if (affected == 0)
        {
            throw new BackendValidationException("Relay ticket already used.", "ticket_consumed");
        }

        return record;
    }

    private string ResolveRelayUrl(HttpContext? httpContext)
    {
        var baseHost = ResolvePublicApiBaseUrl(httpContext);
        // WebSocket scheme: https → wss, http → ws (spec §4.5 requires wss://).
        var wsScheme = baseHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "wss://"
            : baseHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "ws://"
            : "wss://";
        var host = baseHost.Substring(baseHost.IndexOf("://", StringComparison.Ordinal) + 3);
        return $"{wsScheme}{host}/api/companion/relay";
    }

    private string ResolvePublicApiBaseUrl(HttpContext? httpContext)
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

    private static string NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new BackendValidationException("Role is required.");
        }
        var lower = role.Trim().ToLowerInvariant();
        if (lower is not ("desktop" or "phone"))
        {
            throw new BackendValidationException("Role must be 'desktop' or 'phone'.");
        }
        return lower;
    }
}

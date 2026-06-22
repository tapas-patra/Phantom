using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class SupportTicketService
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "open",
        "investigating",
        "waiting_for_user",
        "resolved",
        "closed"
    };

    private static readonly HashSet<string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "low",
        "normal",
        "high",
        "urgent"
    };

    private readonly SupportTicketRepository _tickets;

    public SupportTicketService(SupportTicketRepository tickets)
    {
        _tickets = tickets;
    }

    public object CreateTicket(DesktopAccountRecord account, SupportTicketCreateRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw new BackendValidationException("Subject is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            throw new BackendValidationException("Describe the issue before creating a ticket.");
        }

        var now = DateTime.UtcNow;
        var ticket = new SupportTicketRecord
        {
            TicketId = $"ticket-{Guid.NewGuid():N}",
            UserId = account.UserId,
            Email = account.Email,
            Subject = request.Subject.Trim(),
            Category = NormalizeCategory(request.Category),
            Priority = NormalizePriority(request.Priority),
            Description = request.Description.Trim(),
            Status = "open",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        _tickets.Save(ticket);
        return ToDto(ticket);
    }

    public object ListUserTickets(string userId, int page, int pageSize)
    {
        var normalizedPage = NormalizePage(page);
        var normalizedPageSize = NormalizePageSize(pageSize);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var items = _tickets.ListForUser(userId, offset, normalizedPageSize).Select(ToDto).ToList();
        var totalCount = _tickets.CountForUser(userId);

        return new
        {
            items,
            page = normalizedPage,
            pageSize = normalizedPageSize,
            totalCount,
            hasNextPage = offset + items.Count < totalCount
        };
    }

    public object ListAdminTickets(string query, string status, int page, int pageSize)
    {
        var normalizedPage = NormalizePage(page);
        var normalizedPageSize = NormalizePageSize(pageSize);
        var normalizedQuery = query?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedStatus = NormalizeStatusFilter(status);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var items = _tickets.ListForAdmin(normalizedQuery, normalizedStatus, offset, normalizedPageSize).Select(ToDto).ToList();
        var totalCount = _tickets.CountForAdmin(normalizedQuery, normalizedStatus);

        return new
        {
            items,
            page = normalizedPage,
            pageSize = normalizedPageSize,
            totalCount,
            hasNextPage = offset + items.Count < totalCount
        };
    }

    public object UpdateTicket(SupportTicketUpdateRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.TicketId))
        {
            throw new BackendValidationException("TicketId is required.");
        }

        var ticket = _tickets.FindByTicketId(request.TicketId)
            ?? throw new BackendValidationException("Support ticket not found.");
        ticket.Status = NormalizeStatus(request.Status);
        ticket.Priority = NormalizePriority(request.Priority);
        ticket.AdminNotes = request.AdminNotes?.Trim() ?? string.Empty;
        ticket.ResolutionSummary = request.ResolutionSummary?.Trim() ?? string.Empty;
        ticket.UpdatedAtUtc = DateTime.UtcNow;
        ticket.LastAdminActionAtUtc = ticket.UpdatedAtUtc;
        ticket.ResolvedAtUtc = string.Equals(ticket.Status, "resolved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ticket.Status, "closed", StringComparison.OrdinalIgnoreCase)
            ? ticket.UpdatedAtUtc
            : null;
        _tickets.Save(ticket);
        return ToDto(ticket);
    }

    private static object ToDto(SupportTicketRecord ticket)
    {
        return new
        {
            ticketId = ticket.TicketId,
            userId = ticket.UserId,
            email = ticket.Email,
            subject = ticket.Subject,
            category = ticket.Category,
            priority = ticket.Priority,
            description = ticket.Description,
            status = ticket.Status,
            adminNotes = ticket.AdminNotes,
            resolutionSummary = ticket.ResolutionSummary,
            createdAtUtc = ticket.CreatedAtUtc,
            updatedAtUtc = ticket.UpdatedAtUtc,
            resolvedAtUtc = ticket.ResolvedAtUtc,
            lastAdminActionAtUtc = ticket.LastAdminActionAtUtc
        };
    }

    private static int NormalizePage(int page) => Math.Max(1, page);

    private static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 1, 50);

    private static string NormalizeCategory(string category)
    {
        return string.IsNullOrWhiteSpace(category) ? "general" : category.Trim().ToLowerInvariant();
    }

    private static string NormalizePriority(string priority)
    {
        var normalized = string.IsNullOrWhiteSpace(priority) ? "normal" : priority.Trim().ToLowerInvariant();
        if (!AllowedPriorities.Contains(normalized))
        {
            throw new BackendValidationException("Unsupported support ticket priority.");
        }

        return normalized;
    }

    private static string NormalizeStatus(string status)
    {
        var normalized = string.IsNullOrWhiteSpace(status) ? "open" : status.Trim().ToLowerInvariant();
        if (!AllowedStatuses.Contains(normalized))
        {
            throw new BackendValidationException("Unsupported support ticket status.");
        }

        return normalized;
    }

    private static string NormalizeStatusFilter(string status)
    {
        if (string.IsNullOrWhiteSpace(status) || string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return NormalizeStatus(status);
    }
}

using System.Net.Mail;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class FeedbackService
{
    private static readonly HashSet<string> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        "product", "feature-request", "bug", "billing", "other"
    };
    private static readonly HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "new", "reviewed", "published", "rejected"
    };

    private readonly FeedbackSubmissionRepository _feedback;

    public FeedbackService(FeedbackSubmissionRepository feedback)
    {
        _feedback = feedback;
    }

    public object Create(FeedbackSubmissionCreateRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.Website))
        {
            return new { accepted = true };
        }

        var name = request.Name?.Trim() ?? string.Empty;
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var message = request.Message?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 80) throw new BackendValidationException("Name must contain 2 to 80 characters.");
        if (!IsEmail(email) || email.Length > 254) throw new BackendValidationException("Enter a valid email address.");
        if (message.Length is < 20 or > 2000) throw new BackendValidationException("Feedback must contain 20 to 2000 characters.");
        if (request.Rating is < 1 or > 5) throw new BackendValidationException("Rating must be between 1 and 5.");
        var category = NormalizeCategory(request.Category);
        var now = DateTime.UtcNow;
        var record = new FeedbackSubmissionRecord
        {
            FeedbackId = $"feedback-{Guid.NewGuid():N}",
            Name = name,
            Email = email,
            Category = category,
            Rating = request.Rating,
            Message = message,
            ConsentToPublish = request.ConsentToPublish,
            Status = "new",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        _feedback.Save(record);
        return new { accepted = true, feedbackId = record.FeedbackId };
    }

    public object ListPublished(int limit) => new
    {
        items = _feedback.ListPublished(limit).Select(ToPublicDto).ToList()
    };

    public object ListAdmin(string status, int page, int pageSize)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var normalizedStatus = NormalizeStatusFilter(status);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var items = _feedback.ListForAdmin(normalizedStatus, offset, normalizedPageSize).Select(ToAdminDto).ToList();
        var totalCount = _feedback.CountForAdmin(normalizedStatus);
        return new
        {
            items,
            page = normalizedPage,
            pageSize = normalizedPageSize,
            totalCount,
            hasNextPage = offset + items.Count < totalCount
        };
    }

    public object Update(FeedbackSubmissionUpdateRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.FeedbackId)) throw new BackendValidationException("FeedbackId is required.");
        var record = _feedback.Find(request.FeedbackId.Trim())
            ?? throw new BackendValidationException("Feedback submission not found.");
        var status = NormalizeStatus(request.Status);
        if (status == "published" && !record.ConsentToPublish)
        {
            throw new BackendValidationException("This person did not consent to public review publication.");
        }
        record.Status = status;
        record.AdminNotes = (request.AdminNotes ?? string.Empty).Trim();
        record.UpdatedAtUtc = DateTime.UtcNow;
        _feedback.Save(record);
        return ToAdminDto(record);
    }

    private static object ToPublicDto(FeedbackSubmissionRecord item) => new
    {
        item.FeedbackId,
        Name = item.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Phantom user",
        item.Rating,
        item.Message,
        item.UpdatedAtUtc
    };

    private static object ToAdminDto(FeedbackSubmissionRecord item) => new
    {
        item.FeedbackId,
        item.Name,
        item.Email,
        item.Category,
        item.Rating,
        item.Message,
        item.ConsentToPublish,
        item.Status,
        item.AdminNotes,
        item.CreatedAtUtc,
        item.UpdatedAtUtc
    };

    private static string NormalizeCategory(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "product" : value.Trim().ToLowerInvariant();
        if (!Categories.Contains(normalized)) throw new BackendValidationException("Unsupported feedback category.");
        return normalized;
    }

    private static string NormalizeStatus(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Statuses.Contains(normalized)) throw new BackendValidationException("Unsupported feedback status.");
        return normalized;
    }

    private static string NormalizeStatusFilter(string value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "all", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : NormalizeStatus(value);

    private static bool IsEmail(string value)
    {
        try { return new MailAddress(value).Address == value; }
        catch { return false; }
    }
}

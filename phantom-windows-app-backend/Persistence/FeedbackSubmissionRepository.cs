using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class FeedbackSubmissionRepository
{
    private readonly PostgresBackendStore _store;

    public FeedbackSubmissionRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public void Save(FeedbackSubmissionRecord feedback)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO feedback_submissions (
    feedback_id, name, email, category, rating, message, consent_to_publish,
    status, admin_notes, created_at_utc, updated_at_utc
) VALUES (
    @feedbackId, @name, @email, @category, @rating, @message, @consentToPublish,
    @status, @adminNotes, @createdAtUtc, @updatedAtUtc
)
ON CONFLICT(feedback_id) DO UPDATE SET
    status = EXCLUDED.status,
    admin_notes = EXCLUDED.admin_notes,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        Bind(command, feedback);
        command.ExecuteNonQuery();
    }

    public FeedbackSubmissionRecord? Find(string feedbackId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM feedback_submissions WHERE feedback_id = @feedbackId LIMIT 1;";
        command.Parameters.AddWithValue("feedbackId", feedbackId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<FeedbackSubmissionRecord> ListForAdmin(string status, int offset, int pageSize)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM feedback_submissions
WHERE (@status = '' OR status = @status)
ORDER BY created_at_utc DESC
OFFSET @offset LIMIT @pageSize;";
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("offset", Math.Max(0, offset));
        command.Parameters.AddWithValue("pageSize", Math.Max(1, pageSize));
        return ReadList(command);
    }

    public int CountForAdmin(string status)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM feedback_submissions WHERE (@status = '' OR status = @status);";
        command.Parameters.AddWithValue("status", status);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    public IReadOnlyList<FeedbackSubmissionRecord> ListPublished(int limit)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM feedback_submissions
WHERE status = 'published' AND consent_to_publish = TRUE
ORDER BY updated_at_utc DESC
LIMIT @limit;";
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 12));
        return ReadList(command);
    }

    private static IReadOnlyList<FeedbackSubmissionRecord> ReadList(NpgsqlCommand command)
    {
        using var reader = command.ExecuteReader();
        var items = new List<FeedbackSubmissionRecord>();
        while (reader.Read()) items.Add(Map(reader));
        return items;
    }

    private static void Bind(NpgsqlCommand command, FeedbackSubmissionRecord feedback)
    {
        command.Parameters.AddWithValue("feedbackId", feedback.FeedbackId);
        command.Parameters.AddWithValue("name", feedback.Name);
        command.Parameters.AddWithValue("email", feedback.Email);
        command.Parameters.AddWithValue("category", feedback.Category);
        command.Parameters.AddWithValue("rating", feedback.Rating);
        command.Parameters.AddWithValue("message", feedback.Message);
        command.Parameters.AddWithValue("consentToPublish", feedback.ConsentToPublish);
        command.Parameters.AddWithValue("status", feedback.Status);
        command.Parameters.AddWithValue("adminNotes", feedback.AdminNotes);
        command.Parameters.AddWithValue("createdAtUtc", feedback.CreatedAtUtc);
        command.Parameters.AddWithValue("updatedAtUtc", feedback.UpdatedAtUtc);
    }

    private static FeedbackSubmissionRecord Map(NpgsqlDataReader reader) => new()
    {
        FeedbackId = reader.GetString(reader.GetOrdinal("feedback_id")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        Email = reader.GetString(reader.GetOrdinal("email")),
        Category = reader.GetString(reader.GetOrdinal("category")),
        Rating = reader.GetInt32(reader.GetOrdinal("rating")),
        Message = reader.GetString(reader.GetOrdinal("message")),
        ConsentToPublish = reader.GetBoolean(reader.GetOrdinal("consent_to_publish")),
        Status = reader.GetString(reader.GetOrdinal("status")),
        AdminNotes = reader.GetString(reader.GetOrdinal("admin_notes")),
        CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
        UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
    };
}

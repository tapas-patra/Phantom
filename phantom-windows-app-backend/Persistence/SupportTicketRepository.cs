using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class SupportTicketRepository
{
    private readonly PostgresBackendStore _store;

    public SupportTicketRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public SupportTicketRecord? FindByTicketId(string ticketId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM support_tickets WHERE ticket_id = @ticketId LIMIT 1;";
        command.Parameters.AddWithValue("ticketId", ticketId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Save(SupportTicketRecord ticket)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO support_tickets (
    ticket_id, user_id, email, subject, category, priority, description, status,
    admin_notes, resolution_summary, created_at_utc, updated_at_utc, resolved_at_utc, last_admin_action_at_utc
) VALUES (
    @ticketId, @userId, @email, @subject, @category, @priority, @description, @status,
    @adminNotes, @resolutionSummary, @createdAtUtc, @updatedAtUtc, @resolvedAtUtc, @lastAdminActionAtUtc
)
ON CONFLICT(ticket_id) DO UPDATE SET
    subject = EXCLUDED.subject,
    category = EXCLUDED.category,
    priority = EXCLUDED.priority,
    description = EXCLUDED.description,
    status = EXCLUDED.status,
    admin_notes = EXCLUDED.admin_notes,
    resolution_summary = EXCLUDED.resolution_summary,
    updated_at_utc = EXCLUDED.updated_at_utc,
    resolved_at_utc = EXCLUDED.resolved_at_utc,
    last_admin_action_at_utc = EXCLUDED.last_admin_action_at_utc;";
        Bind(command, ticket);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<SupportTicketRecord> ListForUser(string userId, int offset, int pageSize)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM support_tickets
WHERE user_id = @userId
ORDER BY updated_at_utc DESC
OFFSET @offset
LIMIT @pageSize;";
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("offset", Math.Max(0, offset));
        command.Parameters.AddWithValue("pageSize", Math.Max(1, pageSize));
        return ReadList(command);
    }

    public int CountForUser(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM support_tickets WHERE user_id = @userId;";
        command.Parameters.AddWithValue("userId", userId);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    public IReadOnlyList<SupportTicketRecord> ListForAdmin(string query, string status, int offset, int pageSize)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM support_tickets
WHERE (@status = '' OR status = @status)
  AND (
    @query = ''
    OR lower(email) LIKE @queryLike
    OR lower(subject) LIKE @queryLike
    OR lower(ticket_id) LIKE @queryLike
    OR lower(category) LIKE @queryLike
  )
ORDER BY
    CASE status
        WHEN 'open' THEN 0
        WHEN 'investigating' THEN 1
        WHEN 'waiting_for_user' THEN 2
        WHEN 'resolved' THEN 3
        ELSE 4
    END,
    updated_at_utc DESC
OFFSET @offset
LIMIT @pageSize;";
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("query", query);
        command.Parameters.AddWithValue("queryLike", $"%{query}%");
        command.Parameters.AddWithValue("offset", Math.Max(0, offset));
        command.Parameters.AddWithValue("pageSize", Math.Max(1, pageSize));
        return ReadList(command);
    }

    public int CountForAdmin(string query, string status)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT COUNT(*) FROM support_tickets
WHERE (@status = '' OR status = @status)
  AND (
    @query = ''
    OR lower(email) LIKE @queryLike
    OR lower(subject) LIKE @queryLike
    OR lower(ticket_id) LIKE @queryLike
    OR lower(category) LIKE @queryLike
  );";
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("query", query);
        command.Parameters.AddWithValue("queryLike", $"%{query}%");
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    private static IReadOnlyList<SupportTicketRecord> ReadList(NpgsqlCommand command)
    {
        using var reader = command.ExecuteReader();
        var items = new List<SupportTicketRecord>();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    private static void Bind(NpgsqlCommand command, SupportTicketRecord ticket)
    {
        command.Parameters.AddWithValue("ticketId", ticket.TicketId);
        command.Parameters.AddWithValue("userId", ticket.UserId);
        command.Parameters.AddWithValue("email", ticket.Email);
        command.Parameters.AddWithValue("subject", ticket.Subject);
        command.Parameters.AddWithValue("category", ticket.Category);
        command.Parameters.AddWithValue("priority", ticket.Priority);
        command.Parameters.AddWithValue("description", ticket.Description);
        command.Parameters.AddWithValue("status", ticket.Status);
        command.Parameters.AddWithValue("adminNotes", ticket.AdminNotes);
        command.Parameters.AddWithValue("resolutionSummary", ticket.ResolutionSummary);
        command.Parameters.AddWithValue("createdAtUtc", ticket.CreatedAtUtc);
        command.Parameters.AddWithValue("updatedAtUtc", ticket.UpdatedAtUtc);
        command.Parameters.AddWithValue("resolvedAtUtc", (object?)ticket.ResolvedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("lastAdminActionAtUtc", (object?)ticket.LastAdminActionAtUtc ?? DBNull.Value);
    }

    private static SupportTicketRecord Map(NpgsqlDataReader reader)
    {
        var resolvedAtOrdinal = reader.GetOrdinal("resolved_at_utc");
        var lastAdminActionOrdinal = reader.GetOrdinal("last_admin_action_at_utc");
        return new SupportTicketRecord
        {
            TicketId = reader.GetString(reader.GetOrdinal("ticket_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            Email = reader.GetString(reader.GetOrdinal("email")),
            Subject = reader.GetString(reader.GetOrdinal("subject")),
            Category = reader.GetString(reader.GetOrdinal("category")),
            Priority = reader.GetString(reader.GetOrdinal("priority")),
            Description = reader.GetString(reader.GetOrdinal("description")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            AdminNotes = reader.GetString(reader.GetOrdinal("admin_notes")),
            ResolutionSummary = reader.GetString(reader.GetOrdinal("resolution_summary")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc")),
            ResolvedAtUtc = reader.IsDBNull(resolvedAtOrdinal) ? null : reader.GetDateTime(resolvedAtOrdinal),
            LastAdminActionAtUtc = reader.IsDBNull(lastAdminActionOrdinal) ? null : reader.GetDateTime(lastAdminActionOrdinal)
        };
    }
}

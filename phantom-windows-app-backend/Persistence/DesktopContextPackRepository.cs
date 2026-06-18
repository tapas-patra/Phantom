using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class DesktopContextPackRepository
{
    private readonly PostgresBackendStore _store;

    public DesktopContextPackRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public DesktopContextPackRecord? FindById(string packId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM desktop_context_packs WHERE pack_id = @packId LIMIT 1;";
        command.Parameters.AddWithValue("packId", packId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<DesktopContextPackRecord> ListByUserId(string userId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM desktop_context_packs
WHERE user_id = @userId
ORDER BY updated_at_utc DESC, name ASC;";
        command.Parameters.AddWithValue("userId", userId);
        using var reader = command.ExecuteReader();
        var items = new List<DesktopContextPackRecord>();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public void Save(DesktopContextPackRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO desktop_context_packs (
    pack_id, user_id, name, resume_text, job_description_text, created_at_utc, updated_at_utc
) VALUES (
    @packId, @userId, @name, @resumeText, @jobDescriptionText, @createdAtUtc, @updatedAtUtc
)
ON CONFLICT (pack_id) DO UPDATE SET
    name = EXCLUDED.name,
    resume_text = EXCLUDED.resume_text,
    job_description_text = EXCLUDED.job_description_text,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        command.Parameters.AddWithValue("packId", record.PackId);
        command.Parameters.AddWithValue("userId", record.UserId);
        command.Parameters.AddWithValue("name", record.Name);
        command.Parameters.AddWithValue("resumeText", record.ResumeText);
        command.Parameters.AddWithValue("jobDescriptionText", record.JobDescriptionText);
        command.Parameters.AddWithValue("createdAtUtc", record.CreatedAtUtc);
        command.Parameters.AddWithValue("updatedAtUtc", record.UpdatedAtUtc);
        command.ExecuteNonQuery();
    }

    public void Delete(string packId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM desktop_context_packs WHERE pack_id = @packId;";
        command.Parameters.AddWithValue("packId", packId);
        command.ExecuteNonQuery();
    }

    private static DesktopContextPackRecord Map(NpgsqlDataReader reader)
    {
        return new DesktopContextPackRecord
        {
            PackId = reader.GetString(reader.GetOrdinal("pack_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            ResumeText = reader.GetString(reader.GetOrdinal("resume_text")),
            JobDescriptionText = reader.GetString(reader.GetOrdinal("job_description_text")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }
}

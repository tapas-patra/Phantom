using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class ManagedAiRuntimeSelectionRepository
{
    public const string GlobalSelectionId = "global";
    private readonly PostgresBackendStore _store;

    public ManagedAiRuntimeSelectionRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public ManagedAiRuntimeSelectionRecord? Get(string selectionId = GlobalSelectionId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT selection_id, provider_id, model_id, updated_at_utc
FROM managed_ai_runtime_selection
WHERE selection_id = @selectionId
LIMIT 1;";
        command.Parameters.AddWithValue("selectionId", selectionId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Save(ManagedAiRuntimeSelectionRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO managed_ai_runtime_selection (
    selection_id, provider_id, model_id, updated_at_utc
) VALUES (
    @selectionId, @providerId, @modelId, @updatedAtUtc
)
ON CONFLICT (selection_id) DO UPDATE SET
    provider_id = EXCLUDED.provider_id,
    model_id = EXCLUDED.model_id,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        command.Parameters.AddWithValue("selectionId", string.IsNullOrWhiteSpace(record.SelectionId) ? GlobalSelectionId : record.SelectionId);
        command.Parameters.AddWithValue("providerId", record.ProviderId);
        command.Parameters.AddWithValue("modelId", record.ModelId);
        command.Parameters.AddWithValue("updatedAtUtc", record.UpdatedAtUtc);
        command.ExecuteNonQuery();
    }

    private static ManagedAiRuntimeSelectionRecord Map(NpgsqlDataReader reader)
    {
        return new ManagedAiRuntimeSelectionRecord
        {
            SelectionId = reader.GetString(reader.GetOrdinal("selection_id")),
            ProviderId = reader.GetString(reader.GetOrdinal("provider_id")),
            ModelId = reader.GetString(reader.GetOrdinal("model_id")),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }
}

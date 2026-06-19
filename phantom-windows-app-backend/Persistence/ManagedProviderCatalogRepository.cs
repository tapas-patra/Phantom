using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class ManagedProviderCatalogRepository
{
    private readonly PostgresBackendStore _store;

    public ManagedProviderCatalogRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public IReadOnlyList<ManagedProviderCatalogRecord> ListAll()
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT provider_id, label, models_json::text AS models_json, refreshed_at_utc
FROM managed_provider_catalog
ORDER BY provider_id ASC;";
        using var reader = command.ExecuteReader();
        var items = new List<ManagedProviderCatalogRecord>();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public ManagedProviderCatalogRecord? FindByProviderId(string providerId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT provider_id, label, models_json::text AS models_json, refreshed_at_utc
FROM managed_provider_catalog
WHERE provider_id = @providerId
LIMIT 1;";
        command.Parameters.AddWithValue("providerId", providerId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Save(ManagedProviderCatalogRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO managed_provider_catalog (
    provider_id, label, models_json, refreshed_at_utc
) VALUES (
    @providerId, @label, CAST(@modelsJson AS jsonb), @refreshedAtUtc
)
ON CONFLICT (provider_id) DO UPDATE SET
    label = EXCLUDED.label,
    models_json = EXCLUDED.models_json,
    refreshed_at_utc = EXCLUDED.refreshed_at_utc;";
        command.Parameters.AddWithValue("providerId", record.ProviderId);
        command.Parameters.AddWithValue("label", record.Label);
        command.Parameters.AddWithValue("modelsJson", record.ModelsJson);
        command.Parameters.AddWithValue("refreshedAtUtc", record.RefreshedAtUtc);
        command.ExecuteNonQuery();
    }

    private static ManagedProviderCatalogRecord Map(NpgsqlDataReader reader)
    {
        return new ManagedProviderCatalogRecord
        {
            ProviderId = reader.GetString(reader.GetOrdinal("provider_id")),
            Label = reader.GetString(reader.GetOrdinal("label")),
            ModelsJson = reader.GetString(reader.GetOrdinal("models_json")),
            RefreshedAtUtc = reader.GetDateTime(reader.GetOrdinal("refreshed_at_utc"))
        };
    }
}

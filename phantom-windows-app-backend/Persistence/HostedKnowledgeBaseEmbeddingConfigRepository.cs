using Npgsql;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class HostedKnowledgeBaseEmbeddingConfigRepository
{
    public const string GlobalConfigId = "global";
    private readonly PostgresBackendStore _store;

    public HostedKnowledgeBaseEmbeddingConfigRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public HostedKnowledgeBaseEmbeddingConfigRecord? Get()
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT config_id, is_enabled, provider_id, base_url, model_id, dimensions, version, batch_size, encrypted_api_key, updated_at_utc
FROM hosted_kb_embedding_config
WHERE config_id = @configId
LIMIT 1;";
        command.Parameters.AddWithValue("configId", GlobalConfigId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Save(HostedKnowledgeBaseEmbeddingConfigRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO hosted_kb_embedding_config (
    config_id, is_enabled, provider_id, base_url, model_id, dimensions, version, batch_size, encrypted_api_key, updated_at_utc
) VALUES (
    @configId, @isEnabled, @providerId, @baseUrl, @modelId, @dimensions, @version, @batchSize, @encryptedApiKey, @updatedAtUtc
)
ON CONFLICT (config_id) DO UPDATE SET
    is_enabled = EXCLUDED.is_enabled,
    provider_id = EXCLUDED.provider_id,
    base_url = EXCLUDED.base_url,
    model_id = EXCLUDED.model_id,
    dimensions = EXCLUDED.dimensions,
    version = EXCLUDED.version,
    batch_size = EXCLUDED.batch_size,
    encrypted_api_key = EXCLUDED.encrypted_api_key,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        command.Parameters.AddWithValue("configId", GlobalConfigId);
        command.Parameters.AddWithValue("isEnabled", record.IsEnabled);
        command.Parameters.AddWithValue("providerId", record.ProviderId);
        command.Parameters.AddWithValue("baseUrl", record.BaseUrl);
        command.Parameters.AddWithValue("modelId", record.ModelId);
        command.Parameters.AddWithValue("dimensions", record.Dimensions);
        command.Parameters.AddWithValue("version", record.Version);
        command.Parameters.AddWithValue("batchSize", record.BatchSize);
        command.Parameters.AddWithValue("encryptedApiKey", record.EncryptedApiKey);
        command.Parameters.AddWithValue("updatedAtUtc", record.UpdatedAtUtc);
        command.ExecuteNonQuery();
    }

    private static HostedKnowledgeBaseEmbeddingConfigRecord Map(NpgsqlDataReader reader)
    {
        return new HostedKnowledgeBaseEmbeddingConfigRecord
        {
            ConfigId = reader.GetString(reader.GetOrdinal("config_id")),
            IsEnabled = reader.GetBoolean(reader.GetOrdinal("is_enabled")),
            ProviderId = reader.GetString(reader.GetOrdinal("provider_id")),
            BaseUrl = reader.GetString(reader.GetOrdinal("base_url")),
            ModelId = reader.GetString(reader.GetOrdinal("model_id")),
            Dimensions = reader.GetInt32(reader.GetOrdinal("dimensions")),
            Version = reader.GetInt32(reader.GetOrdinal("version")),
            BatchSize = reader.GetInt32(reader.GetOrdinal("batch_size")),
            EncryptedApiKey = reader.GetString(reader.GetOrdinal("encrypted_api_key")),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }
}

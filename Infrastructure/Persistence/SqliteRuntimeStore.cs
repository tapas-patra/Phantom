using System;
using Microsoft.Data.Sqlite;

namespace SecureOverlay.Infrastructure.Persistence
{
    public sealed class SqliteRuntimeStore
    {
        public const string SettingsKey = "settings";
        public const string ConversationCacheKey = "conversation_cache";
        public const string AuthSessionKey = "auth_session";
        public const string AccountCacheKey = "account_cache";
        public const string InterviewSessionKey = "interview_session";
        public const string ContextPackStateKey = "context_pack_state";
        public const string DeviceProfileKey = "device_profile";
        public const string UsageReconciliationQueueKey = "usage_reconciliation_queue";
        public const string TelemetryQueueKey = "telemetry_queue";

        private readonly string _databasePath;

        public SqliteRuntimeStore(string databasePath)
        {
            _databasePath = databasePath;
        }

        public SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection($"Data Source={_databasePath}");
            connection.Open();
            return connection;
        }

        public void EnsureSchema()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS app_state (
    state_key TEXT PRIMARY KEY,
    payload_json TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
";
            command.ExecuteNonQuery();
        }

        public bool HasEntry(string stateKey)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM app_state WHERE state_key = $key;";
            command.Parameters.AddWithValue("$key", stateKey);
            var count = Convert.ToInt32(command.ExecuteScalar());
            return count > 0;
        }

        public string? ReadPayload(string stateKey)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload_json FROM app_state WHERE state_key = $key LIMIT 1;";
            command.Parameters.AddWithValue("$key", stateKey);
            return command.ExecuteScalar() as string;
        }

        public void UpsertPayload(string stateKey, string payloadJson, DateTime updatedAtUtc)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO app_state (state_key, payload_json, updated_at)
VALUES ($key, $payload, $updatedAt)
ON CONFLICT(state_key) DO UPDATE SET
    payload_json = excluded.payload_json,
    updated_at = excluded.updated_at;
";
            command.Parameters.AddWithValue("$key", stateKey);
            command.Parameters.AddWithValue("$payload", payloadJson);
            command.Parameters.AddWithValue("$updatedAt", updatedAtUtc.ToString("O"));
            command.ExecuteNonQuery();
        }

        public void DeletePayload(string stateKey)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM app_state WHERE state_key = $key;";
            command.Parameters.AddWithValue("$key", stateKey);
            command.ExecuteNonQuery();
        }
    }
}

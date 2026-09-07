using System.Text.Json;
using NpgsqlTypes;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class AdminAuditRepository
{
    private readonly PostgresBackendStore _store;

    public AdminAuditRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public void Record(
        string adminId,
        string adminEmail,
        string method,
        string path,
        string targetUserId,
        string reason,
        string correlationId,
        string ipAddress,
        bool succeeded,
        int? statusCode)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO admin_action_audit (
    audit_id, admin_id, admin_email, method, path, target_user_id, reason,
    correlation_id, ip_address, succeeded, status_code, metadata_json, created_at_utc
) VALUES (
    @auditId, @adminId, @adminEmail, @method, @path, @targetUserId, @reason,
    @correlationId, @ipAddress, @succeeded, @statusCode, @metadataJson, @createdAtUtc
);";
        command.Parameters.AddWithValue("auditId", $"audit-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("adminId", adminId);
        command.Parameters.AddWithValue("adminEmail", adminEmail);
        command.Parameters.AddWithValue("method", method);
        command.Parameters.AddWithValue("path", path);
        command.Parameters.AddWithValue("targetUserId", targetUserId);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("correlationId", correlationId);
        command.Parameters.AddWithValue("ipAddress", ipAddress);
        command.Parameters.AddWithValue("succeeded", succeeded);
        command.Parameters.AddWithValue("statusCode", (object?)statusCode ?? DBNull.Value);
        command.Parameters.AddWithValue("metadataJson", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new { source = "admin_api" }));
        command.Parameters.AddWithValue("createdAtUtc", DateTime.UtcNow);
        command.ExecuteNonQuery();
    }

    public object ListRecent(int page, int pageSize)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        using var connection = _store.OpenConnection();
        var items = new List<object>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT audit_id, admin_email, method, path, target_user_id, reason, correlation_id,
       ip_address, succeeded, status_code, created_at_utc
FROM admin_action_audit
ORDER BY created_at_utc DESC
OFFSET @offset
LIMIT @pageSize;";
            command.Parameters.AddWithValue("offset", offset);
            command.Parameters.AddWithValue("pageSize", normalizedPageSize);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var statusOrdinal = reader.GetOrdinal("status_code");
                items.Add(new
                {
                    auditId = reader.GetString(reader.GetOrdinal("audit_id")),
                    adminEmail = reader.GetString(reader.GetOrdinal("admin_email")),
                    method = reader.GetString(reader.GetOrdinal("method")),
                    path = reader.GetString(reader.GetOrdinal("path")),
                    targetUserId = reader.GetString(reader.GetOrdinal("target_user_id")),
                    reason = reader.GetString(reader.GetOrdinal("reason")),
                    correlationId = reader.GetString(reader.GetOrdinal("correlation_id")),
                    ipAddress = reader.GetString(reader.GetOrdinal("ip_address")),
                    succeeded = reader.GetBoolean(reader.GetOrdinal("succeeded")),
                    statusCode = reader.IsDBNull(statusOrdinal) ? (int?)null : reader.GetInt32(statusOrdinal),
                    createdAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc"))
                });
            }
        }

        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM admin_action_audit;";
        var totalCount = Convert.ToInt32(countCommand.ExecuteScalar() ?? 0);
        return new
        {
            items,
            page = normalizedPage,
            pageSize = normalizedPageSize,
            totalCount,
            hasNextPage = offset + items.Count < totalCount
        };
    }
}

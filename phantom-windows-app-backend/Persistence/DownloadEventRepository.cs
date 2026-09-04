namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class DownloadEventRepository
{
    private readonly PostgresBackendStore _store;

    public DownloadEventRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public void Record(string tokenNonce, string userId, string platform)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO download_events (download_event_id, token_nonce, user_id, platform, downloaded_at_utc)
VALUES (@eventId, @tokenNonce, @userId, @platform, NOW())
ON CONFLICT(token_nonce) DO NOTHING;";
        command.Parameters.AddWithValue("eventId", $"download-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("tokenNonce", tokenNonce);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("platform", platform);
        command.ExecuteNonQuery();
    }
}

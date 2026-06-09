using Microsoft.Data.Sqlite;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class UsageLedgerRepository
{
    private readonly SqliteBackendStore _store;

    public UsageLedgerRepository(SqliteBackendStore store)
    {
        _store = store;
    }

    public UsageLedgerRecord? FindBySessionId(string sessionId)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM usage_ledger WHERE session_id = $sessionId LIMIT 1;";
        command.Parameters.AddWithValue("$sessionId", sessionId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Save(UsageLedgerRecord record)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO usage_ledger (
    ledger_entry_id, user_id, session_id, started_at_utc, ended_at_utc,
    charged_credits, charged_blocks, added_premium_debt, created_at_utc
) VALUES (
    $ledgerEntryId, $userId, $sessionId, $startedAt, $endedAt,
    $chargedCredits, $chargedBlocks, $addedDebt, $createdAt
);";
        command.Parameters.AddWithValue("$ledgerEntryId", record.LedgerEntryId);
        command.Parameters.AddWithValue("$userId", record.UserId);
        command.Parameters.AddWithValue("$sessionId", record.SessionId);
        command.Parameters.AddWithValue("$startedAt", record.StartedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$endedAt", record.EndedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$chargedCredits", record.ChargedCredits.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$chargedBlocks", record.ChargedBlocks);
        command.Parameters.AddWithValue("$addedDebt", record.AddedPremiumDebt.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$createdAt", record.CreatedAtUtc.ToString("O"));
        command.ExecuteNonQuery();
    }

    public List<UsageLedgerRecord> ListRecentForUser(string userId, int maxCount)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM usage_ledger
WHERE user_id = $userId
ORDER BY created_at_utc DESC
LIMIT $maxCount;";
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$maxCount", Math.Max(1, maxCount));
        using var reader = command.ExecuteReader();
        var items = new List<UsageLedgerRecord>();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    private static UsageLedgerRecord Map(SqliteDataReader reader)
    {
        return new UsageLedgerRecord
        {
            LedgerEntryId = reader.GetString(reader.GetOrdinal("ledger_entry_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            SessionId = reader.GetString(reader.GetOrdinal("session_id")),
            StartedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("started_at_utc")), null, System.Globalization.DateTimeStyles.RoundtripKind),
            EndedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("ended_at_utc")), null, System.Globalization.DateTimeStyles.RoundtripKind),
            ChargedCredits = decimal.Parse(reader.GetString(reader.GetOrdinal("charged_credits")), System.Globalization.CultureInfo.InvariantCulture),
            ChargedBlocks = reader.GetInt32(reader.GetOrdinal("charged_blocks")),
            AddedPremiumDebt = decimal.Parse(reader.GetString(reader.GetOrdinal("added_premium_debt")), System.Globalization.CultureInfo.InvariantCulture),
            CreatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at_utc")), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }
}

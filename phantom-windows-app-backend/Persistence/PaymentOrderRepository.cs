using Npgsql;
using NpgsqlTypes;
using Phantom.WindowsApp.Backend.Domain;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class PaymentOrderRepository
{
    private readonly PostgresBackendStore _store;

    public PaymentOrderRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public PaymentOrderRecord? FindByCheckoutId(string checkoutId)
    {
        using var connection = _store.OpenConnection();
        return FindByCheckoutId(checkoutId, connection, transaction: null, forUpdate: false);
    }

    public PaymentOrderRecord? FindByCheckoutId(
        string checkoutId,
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        bool forUpdate)
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"SELECT * FROM payment_orders WHERE checkout_id = @checkoutId LIMIT 1{(forUpdate ? " FOR UPDATE" : string.Empty)};";
        command.Parameters.AddWithValue("checkoutId", checkoutId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public PaymentOrderRecord? FindByRazorpayOrderId(string razorpayOrderId)
    {
        using var connection = _store.OpenConnection();
        return FindByRazorpayOrderId(razorpayOrderId, connection, transaction: null, forUpdate: false);
    }

    public PaymentOrderRecord? FindByRazorpayOrderId(
        string razorpayOrderId,
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        bool forUpdate)
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"SELECT * FROM payment_orders WHERE razorpay_order_id = @razorpayOrderId LIMIT 1{(forUpdate ? " FOR UPDATE" : string.Empty)};";
        command.Parameters.AddWithValue("razorpayOrderId", razorpayOrderId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<PaymentOrderRecord> ListRecentForUser(string userId, int maxCount)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM payment_orders
WHERE user_id = @userId
ORDER BY created_at_utc DESC
LIMIT @maxCount;";
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("maxCount", Math.Max(1, maxCount));
        using var reader = command.ExecuteReader();
        var items = new List<PaymentOrderRecord>();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public IReadOnlyList<PaymentOrderRecord> ListRecentOrders(int maxCount)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM payment_orders
ORDER BY created_at_utc DESC
LIMIT @maxCount;";
        command.Parameters.AddWithValue("maxCount", Math.Max(1, maxCount));
        using var reader = command.ExecuteReader();
        var items = new List<PaymentOrderRecord>();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public void Save(PaymentOrderRecord record)
    {
        using var connection = _store.OpenConnection();
        Save(record, connection, transaction: null);
    }

    public void Save(PaymentOrderRecord record, NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = @"
INSERT INTO payment_orders (
    checkout_id, user_id, email, target, pack_code, display_label, currency, amount_minor, credits,
    premium_debt_credits_covered, razorpay_order_id, razorpay_payment_id, razorpay_signature, status,
    client_confirmed, credited_at_utc, created_at_utc, updated_at_utc
) VALUES (
    @checkoutId, @userId, @email, @target, @packCode, @displayLabel, @currency, @amountMinor, @credits,
    @premiumDebtCreditsCovered, @razorpayOrderId, @razorpayPaymentId, @razorpaySignature, @status,
    @clientConfirmed, @creditedAtUtc, @createdAtUtc, @updatedAtUtc
)
ON CONFLICT(checkout_id) DO UPDATE SET
    razorpay_payment_id = EXCLUDED.razorpay_payment_id,
    razorpay_signature = EXCLUDED.razorpay_signature,
    status = EXCLUDED.status,
    client_confirmed = EXCLUDED.client_confirmed,
    credited_at_utc = EXCLUDED.credited_at_utc,
    updated_at_utc = EXCLUDED.updated_at_utc;";
        Bind(command, record);
        command.ExecuteNonQuery();
    }

    public PaymentWebhookEventRecord? FindWebhookEvent(string externalEventId)
    {
        using var connection = _store.OpenConnection();
        return FindWebhookEvent(externalEventId, connection, transaction: null, forUpdate: false);
    }

    public PaymentWebhookEventRecord? FindWebhookEvent(
        string externalEventId,
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        bool forUpdate)
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = $"SELECT * FROM payment_webhook_events WHERE external_event_id = @externalEventId LIMIT 1{(forUpdate ? " FOR UPDATE" : string.Empty)};";
        command.Parameters.AddWithValue("externalEventId", externalEventId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapEvent(reader) : null;
    }

    public void SaveWebhookEvent(PaymentWebhookEventRecord record)
    {
        using var connection = _store.OpenConnection();
        SaveWebhookEvent(record, connection, transaction: null);
    }

    public void SaveWebhookEvent(PaymentWebhookEventRecord record, NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        using var command = CreateCommand(connection, transaction);
        command.CommandText = @"
INSERT INTO payment_webhook_events (
    event_record_id, external_event_id, event_type, payload_json, created_at_utc, processed_at_utc
) VALUES (
    @eventRecordId, @externalEventId, @eventType, @payloadJson, @createdAtUtc, @processedAtUtc
)
ON CONFLICT(external_event_id) DO UPDATE SET
    processed_at_utc = EXCLUDED.processed_at_utc;";
        command.Parameters.AddWithValue("eventRecordId", record.EventRecordId);
        command.Parameters.AddWithValue("externalEventId", record.ExternalEventId);
        command.Parameters.AddWithValue("eventType", record.EventType);
        command.Parameters.Add("payloadJson", NpgsqlDbType.Jsonb).Value = record.PayloadJson;
        command.Parameters.AddWithValue("createdAtUtc", record.CreatedAtUtc);
        command.Parameters.AddWithValue("processedAtUtc", (object?)record.ProcessedAtUtc ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<PaymentWebhookEventRecord> ListRecentWebhookEvents(int maxCount)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT * FROM payment_webhook_events
ORDER BY created_at_utc DESC
LIMIT @maxCount;";
        command.Parameters.AddWithValue("maxCount", Math.Max(1, maxCount));
        using var reader = command.ExecuteReader();
        var items = new List<PaymentWebhookEventRecord>();
        while (reader.Read())
        {
            items.Add(MapEvent(reader));
        }

        return items;
    }

    private static NpgsqlCommand CreateCommand(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        return command;
    }

    private static void Bind(NpgsqlCommand command, PaymentOrderRecord record)
    {
        command.Parameters.AddWithValue("checkoutId", record.CheckoutId);
        command.Parameters.AddWithValue("userId", record.UserId);
        command.Parameters.AddWithValue("email", record.Email);
        command.Parameters.AddWithValue("target", record.Target);
        command.Parameters.AddWithValue("packCode", record.PackCode);
        command.Parameters.AddWithValue("displayLabel", record.DisplayLabel);
        command.Parameters.AddWithValue("currency", record.Currency);
        command.Parameters.AddWithValue("amountMinor", record.AmountMinor);
        command.Parameters.AddWithValue("credits", record.Credits);
        command.Parameters.AddWithValue("premiumDebtCreditsCovered", record.PremiumDebtCreditsCovered);
        command.Parameters.AddWithValue("razorpayOrderId", record.RazorpayOrderId);
        command.Parameters.AddWithValue("razorpayPaymentId", record.RazorpayPaymentId);
        command.Parameters.AddWithValue("razorpaySignature", record.RazorpaySignature);
        command.Parameters.AddWithValue("status", record.Status);
        command.Parameters.AddWithValue("clientConfirmed", record.ClientConfirmed);
        command.Parameters.AddWithValue("creditedAtUtc", (object?)record.CreditedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("createdAtUtc", record.CreatedAtUtc);
        command.Parameters.AddWithValue("updatedAtUtc", record.UpdatedAtUtc);
    }

    private static PaymentOrderRecord Map(NpgsqlDataReader reader)
    {
        return new PaymentOrderRecord
        {
            CheckoutId = reader.GetString(reader.GetOrdinal("checkout_id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            Email = reader.GetString(reader.GetOrdinal("email")),
            Target = reader.GetString(reader.GetOrdinal("target")),
            PackCode = reader.GetString(reader.GetOrdinal("pack_code")),
            DisplayLabel = reader.GetString(reader.GetOrdinal("display_label")),
            Currency = reader.GetString(reader.GetOrdinal("currency")),
            AmountMinor = reader.GetInt32(reader.GetOrdinal("amount_minor")),
            Credits = reader.GetDecimal(reader.GetOrdinal("credits")),
            PremiumDebtCreditsCovered = reader.GetDecimal(reader.GetOrdinal("premium_debt_credits_covered")),
            RazorpayOrderId = reader.GetString(reader.GetOrdinal("razorpay_order_id")),
            RazorpayPaymentId = reader.GetString(reader.GetOrdinal("razorpay_payment_id")),
            RazorpaySignature = reader.GetString(reader.GetOrdinal("razorpay_signature")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            ClientConfirmed = reader.GetBoolean(reader.GetOrdinal("client_confirmed")),
            CreditedAtUtc = reader.IsDBNull(reader.GetOrdinal("credited_at_utc"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("credited_at_utc")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
        };
    }

    private static PaymentWebhookEventRecord MapEvent(NpgsqlDataReader reader)
    {
        return new PaymentWebhookEventRecord
        {
            EventRecordId = reader.GetString(reader.GetOrdinal("event_record_id")),
            ExternalEventId = reader.GetString(reader.GetOrdinal("external_event_id")),
            EventType = reader.GetString(reader.GetOrdinal("event_type")),
            PayloadJson = reader.GetString(reader.GetOrdinal("payload_json")),
            CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            ProcessedAtUtc = reader.IsDBNull(reader.GetOrdinal("processed_at_utc"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("processed_at_utc"))
        };
    }
}

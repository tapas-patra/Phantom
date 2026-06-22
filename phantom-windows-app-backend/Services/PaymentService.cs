using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class PaymentService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly BackendOptions _options;
    private readonly PostgresBackendStore _store;
    private readonly AccountRepository _accounts;
    private readonly PaymentOrderRepository _paymentOrders;
    private readonly UsageLedgerRepository _usageLedger;
    private readonly PaymentCatalog _catalog;

    public PaymentService(
        BackendOptions options,
        PostgresBackendStore store,
        AccountRepository accounts,
        PaymentOrderRepository paymentOrders,
        UsageLedgerRepository usageLedger,
        PaymentCatalog catalog)
    {
        _options = options;
        _store = store;
        _accounts = accounts;
        _paymentOrders = paymentOrders;
        _usageLedger = usageLedger;
        _catalog = catalog;
    }

    public PaymentCatalogResponseDto GetCatalog(DesktopAccountRecord account)
    {
        if (!_options.HasRazorpayCredentials)
        {
            throw new BackendValidationException("Razorpay checkout is not configured yet.");
        }

        return _catalog.BuildCatalog(account, _options.RazorpayKeyId);
    }

    public async Task<PaymentCheckoutSessionDto> CreateCheckoutAsync(DesktopAccountRecord account, PaymentCheckoutCreateRequestDto request, CancellationToken cancellationToken)
    {
        if (!_options.HasRazorpayCredentials)
        {
            throw new BackendValidationException("Razorpay checkout is not configured yet.");
        }

        if (!account.PhoneVerified)
        {
            throw new BackendValidationException("Phone verification is required before purchasing credits.");
        }

        var now = DateTime.UtcNow;
        var checkoutId = $"checkout-{Guid.NewGuid():N}";
        var orderLabel = string.Empty;
        var amountMinor = 0;
        var credits = 0m;
        var premiumDebtCreditsCovered = 0m;
        var target = request.Target?.Trim() ?? string.Empty;
        var packCode = request.PackCode?.Trim() ?? string.Empty;

        if (string.Equals(target, "premium_debt_settlement", StringComparison.OrdinalIgnoreCase))
        {
            premiumDebtCreditsCovered = Math.Max(0m, account.PremiumNegativeCredits);
            if (premiumDebtCreditsCovered <= 0m)
            {
                throw new BackendValidationException("There is no Premium debt to settle.");
            }

            amountMinor = _catalog.GetDebtSettlementAmountMinor(premiumDebtCreditsCovered);
            credits = premiumDebtCreditsCovered;
            orderLabel = "Premium Debt Settlement";
            packCode = "premium_debt_settlement";
        }
        else
        {
            var pack = _catalog.ResolvePack(target, packCode);
            amountMinor = pack.AmountMinor;
            credits = pack.Credits;
            orderLabel = pack.Label;
        }

        var razorpayOrderId = await CreateRazorpayOrderAsync(BuildRazorpayReceipt(checkoutId), amountMinor, cancellationToken);
        var record = new PaymentOrderRecord
        {
            CheckoutId = checkoutId,
            UserId = account.UserId,
            Email = account.Email,
            Target = target,
            PackCode = packCode,
            DisplayLabel = orderLabel,
            Currency = "INR",
            AmountMinor = amountMinor,
            Credits = credits,
            PremiumDebtCreditsCovered = premiumDebtCreditsCovered,
            RazorpayOrderId = razorpayOrderId,
            Status = "created",
            ClientConfirmed = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        _paymentOrders.Save(record);

        return new PaymentCheckoutSessionDto
        {
            CheckoutId = record.CheckoutId,
            RazorpayOrderId = record.RazorpayOrderId,
            RazorpayKeyId = _options.RazorpayKeyId,
            Target = record.Target,
            PackCode = record.PackCode,
            DisplayLabel = record.DisplayLabel,
            AmountMinor = record.AmountMinor,
            Currency = record.Currency,
            Credits = record.Credits,
            PremiumDebtCreditsCovered = record.PremiumDebtCreditsCovered,
            Status = record.Status
        };
    }

    public object ConfirmClientPayment(DesktopAccountRecord account, PaymentClientConfirmationRequestDto request)
    {
        using var connection = _store.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var order = _paymentOrders.FindByCheckoutId(request.CheckoutId?.Trim() ?? string.Empty, connection, transaction, forUpdate: true)
            ?? throw new BackendValidationException("Checkout not found.");
        if (!string.Equals(order.UserId, account.UserId, StringComparison.Ordinal))
        {
            throw new BackendValidationException("Checkout does not belong to this account.");
        }

        if (!string.Equals(order.RazorpayOrderId, request.RazorpayOrderId?.Trim(), StringComparison.Ordinal))
        {
            throw new BackendValidationException("Checkout order mismatch.");
        }

        VerifyPaymentSignature(order.RazorpayOrderId, request.RazorpayPaymentId, request.RazorpaySignature);

        order.RazorpayPaymentId = request.RazorpayPaymentId.Trim();
        order.RazorpaySignature = request.RazorpaySignature.Trim();
        order.ClientConfirmed = true;
        ApplyConfirmedOrderCredit(order, connection, transaction);
        transaction.Commit();

        return new
        {
            acknowledged = true,
            checkoutId = order.CheckoutId,
            status = order.Status,
            creditedAtUtc = order.CreditedAtUtc,
            message = order.CreditedAtUtc.HasValue
                ? "Payment verified and wallet credits applied."
                : "Payment acknowledged. Wallet credit is pending backend confirmation."
        };
    }

    public IReadOnlyList<object> ListOrdersForUser(string userId, int maxCount)
    {
        return _paymentOrders.ListRecentForUser(userId, maxCount)
            .Select(order => (object)new
            {
                order.CheckoutId,
                order.Target,
                order.PackCode,
                order.DisplayLabel,
                order.AmountMinor,
                amountInr = order.AmountMinor / 100m,
                order.Credits,
                order.PremiumDebtCreditsCovered,
                order.Status,
                order.ClientConfirmed,
                order.CreditedAtUtc,
                order.CreatedAtUtc
            })
            .ToList();
    }

    public async Task<object> ProcessWebhookAsync(string payload, string signature, CancellationToken cancellationToken)
    {
        if (!_options.HasRazorpayWebhookSecret)
        {
            throw new BackendValidationException("Razorpay webhook secret is not configured.");
        }

        VerifyWebhookSignature(payload, signature);

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        var eventType = root.GetProperty("event").GetString() ?? string.Empty;
        var externalEventId = root.TryGetProperty("id", out var idElement)
            ? idElement.GetString() ?? $"webhook-{Guid.NewGuid():N}"
            : $"webhook-{Guid.NewGuid():N}";

        using var connection = _store.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existingEvent = _paymentOrders.FindWebhookEvent(externalEventId, connection, transaction, forUpdate: true);
        if (existingEvent != null && existingEvent.ProcessedAtUtc.HasValue)
        {
            transaction.Commit();
            return new { processed = true, duplicate = true, eventType };
        }

        var eventRecord = existingEvent ?? new PaymentWebhookEventRecord
        {
            EventRecordId = $"payment-webhook-{Guid.NewGuid():N}",
            ExternalEventId = externalEventId,
            EventType = eventType,
            PayloadJson = payload,
            CreatedAtUtc = DateTime.UtcNow
        };

        if (string.Equals(eventType, "payment.captured", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, "order.paid", StringComparison.OrdinalIgnoreCase))
        {
            ApplyOrderPaidEvent(root, connection, transaction);
        }

        eventRecord.ProcessedAtUtc = DateTime.UtcNow;
        _paymentOrders.SaveWebhookEvent(eventRecord, connection, transaction);
        transaction.Commit();
        await Task.CompletedTask;
        return new { processed = true, duplicate = false, eventType };
    }

    private void ApplyOrderPaidEvent(JsonElement root, Npgsql.NpgsqlConnection connection, Npgsql.NpgsqlTransaction transaction)
    {
        var paymentEntity = root.GetProperty("payload").GetProperty("payment").GetProperty("entity");
        var razorpayOrderId = paymentEntity.GetProperty("order_id").GetString() ?? string.Empty;
        var razorpayPaymentId = paymentEntity.GetProperty("id").GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(razorpayOrderId))
        {
            return;
        }

        var order = _paymentOrders.FindByRazorpayOrderId(razorpayOrderId, connection, transaction, forUpdate: true);
        if (order == null)
        {
            return;
        }
        order.RazorpayPaymentId = string.IsNullOrWhiteSpace(order.RazorpayPaymentId) ? razorpayPaymentId : order.RazorpayPaymentId;
        ApplyConfirmedOrderCredit(order, connection, transaction);
    }

    private void ApplyConfirmedOrderCredit(
        PaymentOrderRecord order,
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction)
    {
        if (order.CreditedAtUtc.HasValue)
        {
            order.Status = "credited";
            order.UpdatedAtUtc = DateTime.UtcNow;
            _paymentOrders.Save(order, connection, transaction);
            return;
        }

        var account = _accounts.FindByUserId(order.UserId, connection, transaction, forUpdate: true);
        if (account == null)
        {
            return;
        }

        var existingLedger = _usageLedger.FindBySessionId($"payment:{order.CheckoutId}", connection, transaction);
        if (existingLedger == null)
        {
            ApplyWalletMutation(account, order, connection, transaction);
            _accounts.Save(account, connection, transaction);
        }

        order.Status = "credited";
        order.CreditedAtUtc = DateTime.UtcNow;
        order.UpdatedAtUtc = DateTime.UtcNow;
        _paymentOrders.Save(order, connection, transaction);
    }

    private void ApplyWalletMutation(
        DesktopAccountRecord account,
        PaymentOrderRecord order,
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction)
    {
        var ledger = new UsageLedgerRecord
        {
            LedgerEntryId = $"ledger-{Guid.NewGuid():N}",
            UserId = account.UserId,
            SessionId = $"payment:{order.CheckoutId}",
            StartedAtUtc = DateTime.UtcNow,
            EndedAtUtc = DateTime.UtcNow,
            ChargedCredits = order.Target switch
            {
                "premium_debt_settlement" => 0m,
                AccessModeResolver.ProByo => -order.Credits,
                _ => -Math.Max(0m, order.Credits - Math.Min(account.PremiumNegativeCredits, order.Credits))
            },
            ChargedBlocks = 0,
            AddedPremiumDebt = order.Target == "premium_debt_settlement"
                ? -Math.Min(account.PremiumNegativeCredits, order.PremiumDebtCreditsCovered)
                : -Math.Min(account.PremiumNegativeCredits, order.Credits),
            CreatedAtUtc = DateTime.UtcNow
        };

        if (string.Equals(order.Target, AccessModeResolver.ProByo, StringComparison.OrdinalIgnoreCase))
        {
            account.ProAvailableCredits += order.Credits;
        }
        else if (string.Equals(order.Target, AccessModeResolver.Premium, StringComparison.OrdinalIgnoreCase))
        {
            var settledDebt = Math.Min(account.PremiumNegativeCredits, order.Credits);
            account.PremiumNegativeCredits -= settledDebt;
            account.PremiumAvailableCredits += Math.Max(0m, order.Credits - settledDebt);
            order.PremiumDebtCreditsCovered = settledDebt;
        }
        else if (string.Equals(order.Target, "premium_debt_settlement", StringComparison.OrdinalIgnoreCase))
        {
            var settledDebt = Math.Min(account.PremiumNegativeCredits, order.PremiumDebtCreditsCovered);
            account.PremiumNegativeCredits -= settledDebt;
            order.PremiumDebtCreditsCovered = settledDebt;
        }

        account.AccessTier = AccessModeResolver.GetEffectiveAccessTier(account);
        account.LastValidatedAtUtc = DateTime.UtcNow;
        account.UpdatedAtUtc = DateTime.UtcNow;
        _usageLedger.Save(ledger, connection, transaction);
    }

    private async Task<string> CreateRazorpayOrderAsync(string receipt, int amountMinor, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            amount = amountMinor,
            currency = "INR",
            receipt
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.razorpay.com/v1/orders");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.RazorpayKeyId}:{_options.RazorpayKeySecret}")));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responsePayload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BackendValidationException($"Razorpay order creation failed: {ExtractRazorpayError(responsePayload)}");
        }

        using var json = JsonDocument.Parse(responsePayload);
        return json.RootElement.GetProperty("id").GetString()
            ?? throw new BackendValidationException("Razorpay did not return an order ID.");
    }

    private void VerifyPaymentSignature(string razorpayOrderId, string razorpayPaymentId, string razorpaySignature)
    {
        var payload = $"{razorpayOrderId}|{razorpayPaymentId}";
        var expected = ComputeHmacHex(payload, _options.RazorpayKeySecret);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(razorpaySignature.Trim())))
        {
            throw new BackendValidationException("Razorpay payment signature validation failed.");
        }
    }

    private void VerifyWebhookSignature(string payload, string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            throw new BackendValidationException("Razorpay webhook signature is missing.");
        }

        var expected = ComputeHmacHex(payload, _options.RazorpayWebhookSecret);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature.Trim())))
        {
            throw new BackendValidationException("Razorpay webhook signature validation failed.");
        }
    }

    private static string ComputeHmacHex(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildRazorpayReceipt(string checkoutId)
    {
        var compact = checkoutId.Replace("checkout-", "chk_", StringComparison.Ordinal);
        return compact.Length <= 40 ? compact : compact[..40];
    }

    private static string ExtractRazorpayError(string responsePayload)
    {
        if (string.IsNullOrWhiteSpace(responsePayload))
        {
            return "empty response from Razorpay";
        }

        try
        {
            using var json = JsonDocument.Parse(responsePayload);
            if (json.RootElement.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.TryGetProperty("description", out var descriptionElement))
                {
                    return descriptionElement.GetString() ?? responsePayload;
                }

                if (errorElement.TryGetProperty("reason", out var reasonElement))
                {
                    return reasonElement.GetString() ?? responsePayload;
                }
            }
        }
        catch
        {
            // Fall back to the raw payload below.
        }

        return responsePayload;
    }
}

using Microsoft.AspNetCore.WebUtilities;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;
using Phantom.WindowsApp.Backend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(BackendOptions.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton<SqliteBackendStore>();
builder.Services.AddSingleton<AccountRepository>();
builder.Services.AddSingleton<AuthSessionRepository>();
builder.Services.AddSingleton<MagicLinkRepository>();
builder.Services.AddSingleton<LockRepository>();
builder.Services.AddSingleton<UsageLedgerRepository>();
builder.Services.AddSingleton<TelemetryRepository>();
builder.Services.AddSingleton<AccountStateService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<UsageReconciliationService>();
builder.Services.AddSingleton<LockService>();
builder.Services.AddSingleton<TelemetryIngestService>();
builder.Services.AddSingleton<AdminService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (BackendValidationException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "phantom-windows-app-backend",
    utc = DateTime.UtcNow
}));

app.MapPost("/api/desktop/auth/login", (
    AuthLoginRequestDto request,
    AccountStateService accounts,
    AuthService auth) =>
{
    var account = accounts.GetForLogin(request);
    var session = auth.CreateSession(
        account,
        request.UseMagicLink ? "magic_link" : "password",
        request.InstallId,
        request.DeviceFingerprintHash);
    return Results.Ok(session);
});

app.MapPost("/api/desktop/auth/magic-link/request", (
    HttpContext httpContext,
    AuthMagicLinkRequestDto request,
    AccountStateService accounts,
    AuthService auth) =>
{
    accounts.GetForLogin(new AuthLoginRequestDto
    {
        Email = request.Email,
        UseMagicLink = true
    });

    var publicBaseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    return Results.Ok(auth.IssueMagicLink(request, publicBaseUrl));
});

app.MapPost("/api/desktop/auth/callback/complete", (
    HttpContext httpContext,
    AuthCallbackCompletionRequestDto request,
    AccountStateService accounts,
    AuthService auth) =>
{
    if (string.IsNullOrWhiteSpace(request.CallbackUri))
    {
        throw new BackendValidationException("CallbackUri is required.");
    }

    var uri = new Uri(request.CallbackUri);
    var query = QueryHelpers.ParseQuery(uri.Query);
    var token = query.TryGetValue("token", out var tokenValues) ? tokenValues.ToString() : string.Empty;
    if (string.IsNullOrWhiteSpace(token))
    {
        throw new BackendValidationException("Magic link token is required.");
    }

    var magicLinks = httpContext.RequestServices.GetRequiredService<MagicLinkRepository>();
    var issued = magicLinks.FindByToken(token) ?? throw new BackendValidationException("Magic link token not found.");
    if (issued.Consumed)
    {
        throw new BackendValidationException("Magic link already consumed.");
    }
    if (issued.ExpiresAtUtc <= DateTime.UtcNow)
    {
        throw new BackendValidationException("Magic link has expired.");
    }
    if (!string.Equals(issued.InstallId, request.InstallId, StringComparison.Ordinal))
    {
        throw new BackendValidationException("Magic link was issued for a different device install.");
    }
    if (!string.Equals(issued.DeviceFingerprintHash, request.DeviceFingerprintHash, StringComparison.Ordinal))
    {
        throw new BackendValidationException("Magic link device fingerprint does not match.");
    }

    var account = accounts.RequireAccountByEmail(issued.Email);
    issued.Consumed = true;
    issued.ConsumedAtUtc = DateTime.UtcNow;
    magicLinks.Save(issued);

    var callbackResult = new AuthCallbackResultDto
    {
        Email = account.Email,
        Status = account.PhoneVerified ? "ready" : "verify",
        PhoneVerified = account.PhoneVerified,
        DeviceInstallId = issued.InstallId,
        DeviceFingerprintHash = issued.DeviceFingerprintHash
    };

    var session = auth.CreateSession(account, "magic_link_callback", issued.InstallId, issued.DeviceFingerprintHash);

    return Results.Ok(new AuthCallbackCompletionResultDto
    {
        Session = session,
        CallbackResult = callbackResult
    });
});

app.MapGet("/magic-link/consume", (HttpContext httpContext) =>
{
    var token = httpContext.Request.Query["token"].ToString();
    if (string.IsNullOrWhiteSpace(token))
    {
        return Results.BadRequest("Missing token.");
    }

    var callbackUri = $"phantom://auth/callback?token={Uri.EscapeDataString(token)}";
    var html = $"""
<!DOCTYPE html>
<html>
<head><meta charset="utf-8"><title>Phantom Magic Link</title></head>
<body style="font-family:Segoe UI, sans-serif; padding:32px; background:#0c1018; color:#fff;">
  <h1>Phantom Magic Link</h1>
  <p>Use the button below to continue sign-in in the desktop app.</p>
  <p><a href="{callbackUri}" style="display:inline-block;padding:12px 18px;background:#1976d2;color:#fff;text-decoration:none;border-radius:8px;">Open Phantom</a></p>
  <p style="margin-top:24px; color:#c6d7e8;">If the app does not open automatically, copy this callback URI into your desktop test flow:</p>
  <pre style="white-space:pre-wrap;color:#ffd89a;">{callbackUri}</pre>
</body>
</html>
""";
    return Results.Content(html, "text/html");
});

app.MapPost("/api/desktop/account/startup-check/session", (
    AuthSessionDto request,
    AccountStateService accounts) =>
{
    var account = accounts.RequireAccount(request.UserId, request.Email);
    return Results.Ok(accounts.BuildStartupSnapshot("session_check", account));
});

app.MapPost("/api/desktop/account/startup-check/callback", (
    AuthCallbackResultDto request,
    AccountStateService accounts) =>
{
    var account = accounts.RequireAccountByEmail(request.Email);
    return Results.Ok(accounts.BuildStartupSnapshot("callback_check", account));
});

app.MapPost("/api/desktop/usage/reconcile", (
    UsageReconciliationRequestDto request,
    UsageReconciliationService usage) =>
{
    return Results.Ok(usage.Reconcile(request));
});

app.MapPost("/api/desktop/telemetry/ingest", (
    TelemetryIngestRequestDto request,
    TelemetryIngestService telemetry) =>
{
    telemetry.Ingest(request);
    return Results.Ok(new { accepted = true });
});

app.MapPost("/api/desktop/locks/acquire", (
    DeviceLockAcquireRequestDto request,
    LockService locks) =>
{
    return Results.Ok(locks.Acquire(request));
});

app.MapPost("/api/desktop/locks/heartbeat", (
    DeviceLockHeartbeatRequestDto request,
    LockService locks) =>
{
    return Results.Ok(locks.Heartbeat(request));
});

app.MapPost("/api/desktop/locks/release", (
    DeviceLockReleaseRequestDto request,
    LockService locks) =>
{
    return Results.Ok(locks.Release(request));
});

app.MapGet("/api/admin/accounts/{userId}", (
    string userId,
    AdminService admin) =>
{
    return Results.Ok(admin.GetAccountSnapshot(userId));
});

app.MapPost("/api/admin/locks/clear", (
    AdminLockClearRequestDto request,
    AdminService admin) =>
{
    return Results.Ok(admin.ClearLock(request));
});

app.MapPost("/api/admin/balance/waive-negative-premium", (
    AdminBalanceWaiverRequestDto request,
    AdminService admin) =>
{
    return Results.Ok(admin.WaiveNegativePremiumBalance(request));
});

app.MapPost("/api/admin/credits/grant", (
    AdminCreditGrantRequestDto request,
    AdminService admin) =>
{
    return Results.Ok(admin.GrantCredits(request));
});

app.Run();

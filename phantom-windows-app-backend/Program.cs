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
    var account = accounts.GetOrCreateForLogin(request);
    var session = auth.CreateSession(
        account,
        request.UseMagicLink ? "magic_link" : "password",
        request.InstallId,
        request.DeviceFingerprintHash);
    return Results.Ok(session);
});

app.MapPost("/api/desktop/auth/callback/complete", (
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
    var email = query.TryGetValue("email", out var emailValues) ? emailValues.ToString() : "callback-user@phantom.app";
    var status = (query.TryGetValue("status", out var statusValues) ? statusValues.ToString() : "ready").ToLowerInvariant();
    var installId = query.TryGetValue("installId", out var installValues) ? installValues.ToString() : "callback-install";
    var fingerprint = query.TryGetValue("deviceFingerprint", out var fingerprintValues) ? fingerprintValues.ToString() : "callback-device";

    var callbackResult = new AuthCallbackResultDto
    {
        Email = email,
        Status = status,
        PhoneVerified = status != "verify",
        DeviceInstallId = installId,
        DeviceFingerprintHash = fingerprint
    };

    var account = accounts.ProjectCallbackState(callbackResult);
    var session = auth.CreateSession(account, "callback", installId, fingerprint);

    return Results.Ok(new AuthCallbackCompletionResultDto
    {
        Session = session,
        CallbackResult = callbackResult
    });
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
    var account = accounts.ProjectCallbackState(request);
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

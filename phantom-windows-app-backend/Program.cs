using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;
using Phantom.WindowsApp.Backend.Services;

var builder = WebApplication.CreateBuilder(args);

var backendOptions = BackendOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(backendOptions);
builder.Services.AddSingleton<PostgresBackendStore>();
builder.Services.AddSingleton<AccountRepository>();
builder.Services.AddSingleton<AuthSessionRepository>();
builder.Services.AddSingleton<MagicLinkRepository>();
builder.Services.AddSingleton<LockRepository>();
builder.Services.AddSingleton<UsageLedgerRepository>();
builder.Services.AddSingleton<TelemetryRepository>();
builder.Services.AddSingleton<LoginAttemptRepository>();
builder.Services.AddSingleton(new PasswordHasher(backendOptions.PasswordIterationCount));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<LoginAttemptService>();
builder.Services.AddSingleton<MagicLinkEmailService>();
builder.Services.AddSingleton<AccountStateService>();
builder.Services.AddSingleton<BootstrapAccountSeeder>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<UsageReconciliationService>();
builder.Services.AddSingleton<LockService>();
builder.Services.AddSingleton<TelemetryIngestService>();
builder.Services.AddSingleton<AdminService>();
builder.Services.AddSingleton<AdminApiKeyFilter>();
builder.Services.AddHostedService<MaintenanceService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", limiterOptions =>
    {
        limiterOptions.PermitLimit = 15;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
        limiterOptions.AutoReplenishment = true;
    });
});

var app = builder.Build();

if (args.Contains("--seed-test-users", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var seeded = scope.ServiceProvider.GetRequiredService<BootstrapAccountSeeder>()
        .SeedDefaultTestUsers();
    Console.WriteLine($"Seeded {seeded} test users into desktop_accounts.");
    return;
}

app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        if (exception is BackendValidationException validationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = validationException.Message });
            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "An unexpected server error occurred."
        });
    });
});

app.UseRateLimiter();

app.MapGet("/health", (PostgresBackendStore store) => Results.Ok(new
{
    status = "ok",
    service = "phantom-windows-app-backend",
    database = store.CanConnect() ? "reachable" : "unreachable",
    utc = DateTime.UtcNow
}));

app.MapGet("/health/ready", (PostgresBackendStore store) =>
    store.CanConnect()
        ? Results.Ok(new { status = "ready", utc = DateTime.UtcNow })
        : Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Database unavailable"))
    .RequireRateLimiting("auth");

app.MapPost("/api/desktop/auth/login", (
    HttpContext httpContext,
    AuthLoginRequestDto request,
    LoginAttemptService attempts,
    AccountStateService accounts,
    AuthService auth,
    AuthSessionRepository sessions,
    TelemetryRepository telemetry) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    attempts.EnsureNotBlocked(email, ipAddress);

    try
    {
        var account = accounts.GetForLogin(request);
        var previousSession = sessions.FindLatestByUser(account.UserId);
        if (previousSession != null
            && !string.Equals(previousSession.DeviceFingerprintHash, request.DeviceFingerprintHash, StringComparison.Ordinal))
        {
            telemetry.Save(new TelemetryEventRecord
            {
                EventId = $"telemetry-{Guid.NewGuid():N}",
                Category = "auth",
                EventName = "new_device_fingerprint_login",
                PayloadJson = $$"""{"email":"{{account.Email}}","previous_fingerprint":"{{previousSession.DeviceFingerprintHash}}","current_fingerprint":"{{request.DeviceFingerprintHash}}"}""",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        var session = auth.CreateSession(
            account,
            "password",
            request.InstallId,
            request.DeviceFingerprintHash);
        attempts.Record(email, ipAddress, succeeded: true);
        return Results.Ok(session);
    }
    catch
    {
        attempts.Record(email, ipAddress, succeeded: false);
        throw;
    }
}).RequireRateLimiting("auth");

app.MapPost("/api/desktop/auth/refresh", (
    AuthRefreshRequestDto request,
    AuthService auth) =>
{
    if (string.IsNullOrWhiteSpace(request.RefreshToken))
    {
        throw new BackendValidationException("RefreshToken is required.");
    }

    return Results.Ok(auth.RefreshSession(
        request.RefreshToken,
        request.InstallId,
        request.DeviceFingerprintHash));
}).RequireRateLimiting("auth");

app.MapPost("/api/desktop/auth/logout", (
    AuthLogoutRequestDto request,
    AuthService auth) =>
{
    if (string.IsNullOrWhiteSpace(request.RefreshToken))
    {
        throw new BackendValidationException("RefreshToken is required.");
    }

    auth.RevokeSession(request.RefreshToken);
    return Results.Ok(new { revoked = true });
}).RequireRateLimiting("auth");

app.MapPost("/api/desktop/auth/magic-link/request", (
    HttpContext httpContext,
    AuthMagicLinkRequestDto request,
    LoginAttemptService attempts,
    AccountStateService accounts,
    AuthService auth) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    attempts.EnsureNotBlocked(email, ipAddress);
    accounts.GetForLogin(new AuthLoginRequestDto
    {
        Email = email,
        UseMagicLink = true
    });

    var publicBaseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    return Results.Ok(auth.IssueMagicLink(request, publicBaseUrl));
}).RequireRateLimiting("auth");

app.MapPost("/api/desktop/auth/callback/complete", (
    AuthCallbackCompletionRequestDto request,
    AccountStateService accounts,
    AuthService auth,
    MagicLinkRepository magicLinks) =>
{
    if (string.IsNullOrWhiteSpace(request.CallbackUri))
    {
        throw new BackendValidationException("CallbackUri is required.");
    }
    if (string.IsNullOrWhiteSpace(request.InstallId) || string.IsNullOrWhiteSpace(request.DeviceFingerprintHash))
    {
        throw new BackendValidationException("InstallId and DeviceFingerprintHash are required.");
    }

    var uri = new Uri(request.CallbackUri);
    var query = QueryHelpers.ParseQuery(uri.Query);
    var token = query.TryGetValue("token", out var tokenValues) ? tokenValues.ToString() : string.Empty;
    if (string.IsNullOrWhiteSpace(token))
    {
        throw new BackendValidationException("Magic link token is required.");
    }

    var issued = auth.RequireMagicLink(token);
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
}).RequireRateLimiting("auth");

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

var adminGroup = app.MapGroup("/api/admin")
    .AddEndpointFilter<AdminApiKeyFilter>();

adminGroup.MapGet("/accounts/{userId}", (
    string userId,
    AdminService admin) =>
{
    return Results.Ok(admin.GetAccountSnapshot(userId));
});

adminGroup.MapGet("/accounts", (AdminService admin) =>
{
    return Results.Ok(admin.ListAccounts());
});

adminGroup.MapPost("/locks/clear", (
    AdminLockClearRequestDto request,
    AdminService admin) =>
{
    return Results.Ok(admin.ClearLock(request));
});

adminGroup.MapPost("/balance/waive-negative-premium", (
    AdminBalanceWaiverRequestDto request,
    AdminService admin) =>
{
    return Results.Ok(admin.WaiveNegativePremiumBalance(request));
});

adminGroup.MapPost("/credits/grant", (
    AdminCreditGrantRequestDto request,
    AdminService admin) =>
{
    return Results.Ok(admin.GrantCredits(request));
});

app.Run();

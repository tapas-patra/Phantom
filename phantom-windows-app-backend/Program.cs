using System.Threading.RateLimiting;
using System.Net.Sockets;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;
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
builder.Services.AddSingleton<EmailVerificationRepository>();
builder.Services.AddSingleton<IntegrationSecretRepository>();
builder.Services.AddSingleton<OAuthPendingStateRepository>();
builder.Services.AddSingleton<ManagedProviderCredentialRepository>();
builder.Services.AddSingleton<LockRepository>();
builder.Services.AddSingleton<UsageLedgerRepository>();
builder.Services.AddSingleton<TelemetryRepository>();
builder.Services.AddSingleton<LoginAttemptRepository>();
builder.Services.AddSingleton(new PasswordHasher(backendOptions.PasswordIterationCount));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<LoginAttemptService>();
builder.Services.AddSingleton<SecretProtector>();
builder.Services.AddSingleton<GoogleMailOAuthService>();
builder.Services.AddSingleton<MagicLinkEmailService>();
builder.Services.AddSingleton<AccountStateService>();
builder.Services.AddSingleton<BootstrapAccountSeeder>();
builder.Services.AddSingleton<RegistrationService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<ManagedAiService>();
builder.Services.AddSingleton<UsageReconciliationService>();
builder.Services.AddSingleton<LockService>();
builder.Services.AddSingleton<TelemetryIngestService>();
builder.Services.AddSingleton<AdminService>();
builder.Services.AddSingleton<AdminApiKeyFilter>();
builder.Services.AddHostedService<MaintenanceService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("website", cors =>
    {
        var origins = new List<string>();
        if (!string.IsNullOrWhiteSpace(backendOptions.PublicWebsiteBaseUrl))
        {
            origins.Add(backendOptions.PublicWebsiteBaseUrl.TrimEnd('/'));
        }

        origins.Add("http://localhost:4173");
        origins.Add("https://localhost:4173");

        cors.WithOrigins(origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

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

        if (exception is NpgsqlException || exception is SocketException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Database unavailable."
            });
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
app.UseCors("website");

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

app.MapPost("/api/desktop/auth/register", (
    HttpContext httpContext,
    AuthRegisterRequestDto request,
    RegistrationService registration) =>
{
    try
    {
        var publicBaseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        return Results.Ok(registration.Register(request, publicBaseUrl));
    }
    catch (BackendValidationException validationException)
    {
        return Results.BadRequest(new { error = validationException.Message });
    }
}).RequireRateLimiting("auth");

app.MapPost("/api/desktop/auth/verify-email/request", (
    HttpContext httpContext,
    AuthEmailVerificationRequestDto request,
    RegistrationService registration) =>
{
    try
    {
        var publicBaseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        return Results.Ok(registration.ResendVerification(request, publicBaseUrl));
    }
    catch (BackendValidationException validationException)
    {
        return Results.BadRequest(new { error = validationException.Message });
    }
}).RequireRateLimiting("auth");

app.MapGet("/email/verify", (
    HttpContext httpContext,
    string token,
    RegistrationService registration,
    BackendOptions options) =>
{
    var result = registration.CompleteVerification(token);
    var redirectBase = string.IsNullOrWhiteSpace(options.PublicWebsiteBaseUrl)
        ? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}"
        : options.PublicWebsiteBaseUrl.TrimEnd('/');
    return Results.Redirect(
        $"{redirectBase}/desktop-return?verification=success&email={Uri.EscapeDataString(result.Email)}");
});

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
    catch (BackendValidationException validationException)
    {
        attempts.Record(email, ipAddress, succeeded: false);
        return Results.BadRequest(new { error = validationException.Message });
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
    try
    {
        telemetry.Ingest(request);
        return Results.Ok(new { accepted = true });
    }
    catch (NpgsqlException)
    {
        return Results.Ok(new { accepted = false, deferred = true, reason = "database_unavailable" });
    }
    catch (SocketException)
    {
        return Results.Ok(new { accepted = false, deferred = true, reason = "database_unavailable" });
    }
});

app.MapGet("/api/desktop/ai/catalog", (
    HttpContext httpContext,
    ManagedAiService managedAi) =>
{
    var account = managedAi.RequireManagedAccountFromAccessToken(httpContext.Request.Headers.Authorization);
    return Results.Ok(managedAi.GetCatalogForAccount(account));
});

app.MapPost("/api/desktop/ai/chat", async (
    HttpContext httpContext,
    DesktopAiChatRequestDto request,
    ManagedAiService managedAi,
    CancellationToken cancellationToken) =>
{
    var account = managedAi.RequireManagedAccountFromAccessToken(httpContext.Request.Headers.Authorization);
    await managedAi.StreamChatAsync(httpContext.Response, account, request, cancellationToken);
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

adminGroup.MapGet("/managed-ai/credentials", (ManagedAiService managedAi) =>
{
    return Results.Ok(managedAi.ListAdminCredentials());
});

adminGroup.MapPost("/managed-ai/credentials", (
    ManagedAiProviderKeyUpsertRequestDto request,
    ManagedAiService managedAi) =>
{
    return Results.Ok(managedAi.UpsertCredential(request));
});

adminGroup.MapDelete("/managed-ai/credentials/{credentialId}", (
    string credentialId,
    ManagedAiService managedAi) =>
{
    managedAi.DeleteCredential(credentialId);
    return Results.Ok(new { deleted = true, credentialId });
});

adminGroup.MapGet("/integrations/gmail/oauth/status", (GoogleMailOAuthService gmailOAuth) =>
{
    return Results.Ok(gmailOAuth.GetStatus());
});

adminGroup.MapPost("/integrations/gmail/oauth/start", (
    HttpContext httpContext,
    GoogleMailOAuthService gmailOAuth) =>
{
    var publicBaseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    return Results.Ok(gmailOAuth.StartAuthorization(publicBaseUrl));
});

app.MapGet("/api/admin/integrations/gmail/oauth/callback", async (
    HttpContext httpContext,
    string code,
    string state,
    GoogleMailOAuthService gmailOAuth,
    BackendOptions options,
    CancellationToken cancellationToken) =>
{
    var publicBaseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    await gmailOAuth.CompleteAuthorizationAsync(code, state, publicBaseUrl, cancellationToken);
    var redirectBase = string.IsNullOrWhiteSpace(options.PublicWebsiteBaseUrl)
        ? publicBaseUrl
        : options.PublicWebsiteBaseUrl.TrimEnd('/');
    return Results.Redirect($"{redirectBase}/desktop-return?gmail_oauth=success");
});

app.Run();

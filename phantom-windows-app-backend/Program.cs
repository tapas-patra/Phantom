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
builder.Services.AddSingleton<DashboardProjectionReplicaStore>();
builder.Services.AddSingleton<AccountRepository>();
builder.Services.AddSingleton<AdminAccountRepository>();
builder.Services.AddSingleton<AdminPasswordResetRepository>();
builder.Services.AddSingleton<UserPasswordResetRepository>();
builder.Services.AddSingleton<AuthSessionRepository>();
builder.Services.AddSingleton<MagicLinkRepository>();
builder.Services.AddSingleton<EmailVerificationRepository>();
builder.Services.AddSingleton<PhoneVerificationRepository>();
builder.Services.AddSingleton<IntegrationSecretRepository>();
builder.Services.AddSingleton<OAuthPendingStateRepository>();
builder.Services.AddSingleton<ManagedProviderCredentialRepository>();
builder.Services.AddSingleton<ManagedProviderCatalogRepository>();
builder.Services.AddSingleton<ManagedAiRuntimeSelectionRepository>();
builder.Services.AddSingleton<ManagedAiLatencyRepository>();
builder.Services.AddSingleton<HostedKnowledgeBaseRepository>();
builder.Services.AddSingleton<HostedKnowledgeBaseEmbeddingConfigRepository>();
builder.Services.AddSingleton<HostedKnowledgeBaseReindexJobRepository>();
builder.Services.AddSingleton<InterviewQuestionBankJobRepository>();
builder.Services.AddSingleton<DesktopContextPackRepository>();
builder.Services.AddSingleton<LockRepository>();
builder.Services.AddSingleton<UsageLedgerRepository>();
builder.Services.AddSingleton<PaymentOrderRepository>();
builder.Services.AddSingleton<SupportTicketRepository>();
builder.Services.AddSingleton<TelemetryRepository>();
builder.Services.AddSingleton<LoginAttemptRepository>();
builder.Services.AddSingleton(new PasswordHasher(backendOptions.PasswordIterationCount));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<DesktopSessionService>();
builder.Services.AddSingleton<LoginAttemptService>();
builder.Services.AddSingleton<AdminBootstrapService>();
builder.Services.AddSingleton<OperationalMetricsService>();
builder.Services.AddSingleton<TwoFactorOtpClient>();
builder.Services.AddSingleton<SecretProtector>();
builder.Services.AddSingleton<GoogleMailOAuthService>();
builder.Services.AddSingleton<MagicLinkEmailService>();
builder.Services.AddSingleton<ManagedAiCatalogService>();
builder.Services.AddSingleton<ManagedAiDiagnosticsService>();
builder.Services.AddSingleton<IKnowledgeBaseEmbeddingService, KnowledgeBaseEmbeddingService>();
builder.Services.AddSingleton<HostedKnowledgeBaseStructuredExtractionService>();
builder.Services.AddSingleton<InterviewQuestionBankService>();
builder.Services.AddSingleton<PaymentCatalog>();
builder.Services.AddSingleton<AccountStateService>();
builder.Services.AddSingleton<BootstrapAccountSeeder>();
builder.Services.AddSingleton<PhoneVerificationService>();
builder.Services.AddSingleton<RegistrationService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<AdminAuthService>();
builder.Services.AddSingleton<ManagedAiService>();
builder.Services.AddSingleton<HostedKnowledgeBaseService>();
builder.Services.AddSingleton<DesktopContextPackService>();
builder.Services.AddSingleton<PaymentService>();
builder.Services.AddSingleton<SupportTicketService>();
builder.Services.AddHostedService<ManagedAiCatalogRefreshWorker>();
builder.Services.AddHostedService<ManagedAiLatencyWorker>();
builder.Services.AddHostedService<HostedKnowledgeBaseReindexWorker>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<InterviewQuestionBankService>());
builder.Services.AddSingleton<UsageReconciliationService>();
builder.Services.AddSingleton<LockService>();
builder.Services.AddSingleton<TelemetryBufferService>();
builder.Services.AddSingleton<TelemetryIngestService>();
builder.Services.AddSingleton<AdminService>();
builder.Services.AddSingleton<BrowserSessionCookieService>();
builder.Services.AddSingleton<AdminApiKeyFilter>();
builder.Services.AddSingleton<InternalApiKeyFilter>();
builder.Services.AddHostedService<MaintenanceService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<TelemetryBufferService>());
builder.Services.AddHostedService<DashboardProjectionReplicatorService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("website", cors =>
    {
        var origins = new List<string>();
        if (!string.IsNullOrWhiteSpace(backendOptions.PublicWebsiteBaseUrl))
        {
            origins.Add(backendOptions.PublicWebsiteBaseUrl.TrimEnd('/'));
        }

        origins.Add(BackendOptions.DefaultPublicWebsiteBaseUrl);
        if (builder.Environment.IsDevelopment())
        {
            origins.Add("http://localhost:4173");
            origins.Add("https://localhost:4173");
        }

        cors.WithOrigins(origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        context.HttpContext.Response.Headers["Retry-After"] = "5";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Too many requests. Please wait a few seconds and try again."
        }, cancellationToken);
    };
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}:{httpContext.Request.Path.Value?.ToLowerInvariant() ?? "/"}",
            partitionKey => new FixedWindowRateLimiterOptions
            {
                PermitLimit = partitionKey.EndsWith("/api/desktop/auth/login", StringComparison.Ordinal)
                    || partitionKey.EndsWith("/api/admin/auth/login", StringComparison.Ordinal)
                    ? 20
                    : 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 2,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));
    options.AddPolicy("desktop-api", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("payments", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("telemetry", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 240,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("admin", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("internal", _ =>
        RateLimitPartition.GetFixedWindowLimiter(
            "internal",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AdminBootstrapService>().EnsureBootstrapAdmin();
}

if (args.Contains("--seed-test-users", StringComparer.OrdinalIgnoreCase))
{
    if (!backendOptions.AllowSeedTestUsers)
    {
        Console.Error.WriteLine(
            "Test-user seeding is disabled. Set PHANTOM_WINDOWS_BACKEND_ALLOW_TEST_USER_SEEDING=true to enable it.");
        return;
    }

    using var scope = app.Services.CreateScope();
    var seeded = scope.ServiceProvider.GetRequiredService<BootstrapAccountSeeder>()
        .SeedDefaultTestUsers();
    Console.WriteLine($"Seeded {seeded} test users into desktop_accounts.");
    return;
}

app.UseCors("website");
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    await next();
});
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

        if (exception is EmbeddingProviderException embeddingException)
        {
            context.Response.StatusCode = embeddingException.IsTransient
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status502BadGateway;
            if (embeddingException.RetryAfterSeconds.HasValue && embeddingException.RetryAfterSeconds.Value > 0)
            {
                context.Response.Headers["Retry-After"] = embeddingException.RetryAfterSeconds.Value.ToString();
            }

            await context.Response.WriteAsJsonAsync(new
            {
                error = embeddingException.Message,
                retryable = embeddingException.IsTransient,
                providerStatusCode = embeddingException.ProviderStatusCode,
                retryAfterSeconds = embeddingException.RetryAfterSeconds
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

app.MapGet("/health", (PostgresBackendStore store) => Results.Ok(new
{
    status = "live",
    service = "phantom-windows-app-backend",
    utc = DateTime.UtcNow
}));

var internalGroup = app.MapGroup("/api/internal")
    .AddEndpointFilter<InternalApiKeyFilter>()
    .RequireRateLimiting("internal");

internalGroup.MapGet("/health/details", (
    PostgresBackendStore store,
    BackendOptions options,
    OperationalMetricsService metrics) =>
{
    var databaseReachable = store.CanConnect();
    return Results.Ok(new
    {
        status = databaseReachable ? "ok" : "degraded",
        service = "phantom-windows-app-backend",
        database = databaseReachable ? "reachable" : "unreachable",
        projectionReplicaEnabled = options.HasDashboardProjectionReplica,
        workers = metrics.CreateSnapshot(),
        utc = DateTime.UtcNow
    });
});

app.MapGet("/health/ready", (
    PostgresBackendStore store,
    BackendOptions options,
    OperationalMetricsService metrics) =>
{
    if (!store.CanConnect())
    {
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Database unavailable");
    }

    if (!metrics.IsReady(options.HasDashboardProjectionReplica))
    {
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Background workers are degraded");
    }

    return Results.Ok(new { status = "ready", utc = DateTime.UtcNow });
})
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

app.MapPost("/api/desktop/auth/phone/send-otp", async (
    PhoneVerificationStartRequestDto request,
    PhoneVerificationService phoneVerification,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await phoneVerification.StartAsync(request, cancellationToken));
}).RequireRateLimiting("auth");

app.MapPost("/api/desktop/auth/phone/verify-otp", async (
    PhoneVerificationConfirmRequestDto request,
    PhoneVerificationService phoneVerification,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await phoneVerification.ConfirmAsync(request, cancellationToken));
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
        $"{redirectBase}/desktop-return?verification=success");
}).RequireRateLimiting("auth");

app.MapPost("/api/desktop/auth/login", (
    HttpContext httpContext,
    AuthLoginRequestDto request,
    LoginAttemptService attempts,
    AccountStateService accounts,
    AuthService auth,
    AuthSessionRepository sessions,
    TelemetryRepository telemetry,
    BrowserSessionCookieService cookies) =>
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
        if (IsBrowserRequest(httpContext))
        {
            cookies.IssueUserCookies(httpContext.Response, session.AccessToken, session.RefreshToken, session.ExpiresAtUtc);
        }

        attempts.Record(email, ipAddress, succeeded: true);
        return Results.Ok(IsBrowserRequest(httpContext) ? SanitizeUserSession(session) : session);
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
    HttpContext httpContext,
    AuthRefreshRequestDto request,
    AuthService auth,
    BrowserSessionCookieService cookies) =>
{
    request.RefreshToken = ResolveRefreshToken(request.RefreshToken, cookies.ReadUserRefreshToken(httpContext.Request));

    if (string.IsNullOrWhiteSpace(request.RefreshToken))
    {
        cookies.ClearUserCookies(httpContext.Response);
        return Results.Unauthorized();
    }

    try
    {
        var session = auth.RefreshSession(
            request.RefreshToken,
            request.InstallId,
            request.DeviceFingerprintHash);
        if (IsBrowserRequest(httpContext))
        {
            cookies.IssueUserCookies(httpContext.Response, session.AccessToken, session.RefreshToken, session.ExpiresAtUtc);
        }

        return Results.Ok(IsBrowserRequest(httpContext) ? SanitizeUserSession(session) : session);
    }
    catch (BackendValidationException)
    {
        cookies.ClearUserCookies(httpContext.Response);
        return Results.Unauthorized();
    }
}).RequireRateLimiting("auth");

app.MapPost("/api/desktop/auth/logout", (
    HttpContext httpContext,
    AuthLogoutRequestDto request,
    AuthService auth,
    BrowserSessionCookieService cookies) =>
{
    request.RefreshToken = ResolveRefreshToken(request.RefreshToken, cookies.ReadUserRefreshToken(httpContext.Request));

    if (string.IsNullOrWhiteSpace(request.RefreshToken))
    {
        cookies.ClearUserCookies(httpContext.Response);
        return Results.Ok(new { revoked = true });
    }

    try
    {
        auth.RevokeSession(request.RefreshToken);
    }
    catch (BackendValidationException)
    {
        // Treat already-missing or already-revoked sessions as logged out.
    }

    cookies.ClearUserCookies(httpContext.Response);
    return Results.Ok(new { revoked = true });
}).RequireRateLimiting("auth");

app.MapPost("/api/desktop/auth/forgot-password", (
    HttpContext httpContext,
    UserPasswordResetStartRequestDto request,
    AuthService auth) =>
{
    var publicBaseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    return Results.Ok(auth.StartPasswordReset(request.Email, publicBaseUrl));
}).RequireRateLimiting("auth");

app.MapPost("/api/desktop/auth/reset-password", (
    UserPasswordResetCompleteRequestDto request,
    AuthService auth) =>
{
    return Results.Ok(auth.CompletePasswordReset(request));
}).RequireRateLimiting("auth");

app.MapGet("/api/desktop/auth/me", (
    HttpContext httpContext,
    BrowserSessionCookieService cookies,
    DesktopSessionService desktopSessions) =>
{
    try
    {
        var session = desktopSessions.RequireSession(cookies.GetUserAuthorizationHeader(httpContext.Request));
        return Results.Ok(new
        {
            session.UserId,
            session.Email,
            AccessToken = cookies.ReadUserAccessToken(httpContext.Request) ?? string.Empty,
            RefreshToken = cookies.ReadUserRefreshToken(httpContext.Request) ?? string.Empty,
            session.AuthMethod,
            session.DeviceInstallId,
            session.DeviceFingerprintHash,
            session.AuthenticatedAtUtc,
            session.ExpiresAtUtc,
            session.IsAuthenticated
        });
    }
    catch (BackendValidationException)
    {
        cookies.ClearUserCookies(httpContext.Response);
        return Results.Unauthorized();
    }
}).RequireRateLimiting("auth");

app.MapPost("/api/admin/auth/login", (
    HttpContext httpContext,
    AdminAuthLoginRequestDto request,
    LoginAttemptService attempts,
    AdminAuthService adminAuth,
    BrowserSessionCookieService cookies) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    attempts.EnsureNotBlocked(email, ipAddress);

    try
    {
        var session = adminAuth.Login(request);
        if (IsBrowserRequest(httpContext))
        {
            cookies.IssueAdminCookies(httpContext.Response, session.AccessToken, session.RefreshToken, session.ExpiresAtUtc);
        }

        attempts.Record(email, ipAddress, succeeded: true);
        return Results.Ok(IsBrowserRequest(httpContext) ? SanitizeAdminSession(session) : session);
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

app.MapPost("/api/admin/auth/refresh", (
    HttpContext httpContext,
    AdminAuthRefreshRequestDto request,
    AdminAuthService adminAuth,
    BrowserSessionCookieService cookies) =>
{
    request.RefreshToken = ResolveRefreshToken(request.RefreshToken, cookies.ReadAdminRefreshToken(httpContext.Request));

    if (string.IsNullOrWhiteSpace(request.RefreshToken))
    {
        cookies.ClearAdminCookies(httpContext.Response);
        return Results.Unauthorized();
    }

    try
    {
        var session = adminAuth.Refresh(request);
        if (IsBrowserRequest(httpContext))
        {
            cookies.IssueAdminCookies(httpContext.Response, session.AccessToken, session.RefreshToken, session.ExpiresAtUtc);
        }

        return Results.Ok(IsBrowserRequest(httpContext) ? SanitizeAdminSession(session) : session);
    }
    catch (BackendValidationException)
    {
        cookies.ClearAdminCookies(httpContext.Response);
        return Results.Unauthorized();
    }
}).RequireRateLimiting("auth");

app.MapPost("/api/admin/auth/logout", (
    HttpContext httpContext,
    AuthLogoutRequestDto request,
    AdminAuthService adminAuth,
    BrowserSessionCookieService cookies) =>
{
    request.RefreshToken = ResolveRefreshToken(request.RefreshToken, cookies.ReadAdminRefreshToken(httpContext.Request));

    if (!string.IsNullOrWhiteSpace(request.RefreshToken))
    {
        try
        {
            adminAuth.Logout(request.RefreshToken);
        }
        catch (BackendValidationException)
        {
            // Treat already-missing or already-revoked sessions as logged out.
        }
    }

    cookies.ClearAdminCookies(httpContext.Response);
    return Results.Ok(new { revoked = true });
}).RequireRateLimiting("auth");

app.MapGet("/api/admin/auth/me", (
    HttpContext httpContext,
    AdminAuthService adminAuth,
    BrowserSessionCookieService cookies) =>
{
    try
    {
        var session = adminAuth.GetSession(cookies.GetAdminAuthorizationHeader(httpContext.Request));
        return Results.Ok(new
        {
            session.AdminId,
            session.Email,
            session.DisplayName,
            session.Role,
            AccessToken = cookies.ReadAdminAccessToken(httpContext.Request) ?? string.Empty,
            RefreshToken = cookies.ReadAdminRefreshToken(httpContext.Request) ?? string.Empty,
            session.AuthMethod,
            session.AuthenticatedAtUtc,
            session.ExpiresAtUtc,
            session.IsAuthenticated
        });
    }
    catch (BackendValidationException)
    {
        cookies.ClearAdminCookies(httpContext.Response);
        return Results.Unauthorized();
    }
}).RequireRateLimiting("auth");

app.MapPost("/api/admin/auth/forgot-password", (
    HttpContext httpContext,
    AdminPasswordResetStartRequestDto request,
    AdminAuthService adminAuth) =>
{
    var publicBaseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    return Results.Ok(adminAuth.StartPasswordReset(request.Email, publicBaseUrl));
}).RequireRateLimiting("auth");

app.MapPost("/api/admin/auth/reset-password", (
    AdminPasswordResetCompleteRequestDto request,
    AdminAuthService adminAuth) =>
{
    return Results.Ok(adminAuth.CompletePasswordReset(request));
}).RequireRateLimiting("auth");

app.MapPost("/api/desktop/account/startup-check/session", (
    HttpContext httpContext,
    AuthSessionDto request,
    DesktopSessionService desktopSessions,
    AccountStateService accounts) =>
{
    var session = desktopSessions.RequireSession(httpContext.Request.Headers.Authorization.ToString());
    if (!string.Equals(request.UserId, session.UserId, StringComparison.Ordinal)
        || !string.Equals(request.Email, session.Email, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(request.DeviceInstallId, session.DeviceInstallId, StringComparison.Ordinal)
        || !string.Equals(request.DeviceFingerprintHash, session.DeviceFingerprintHash, StringComparison.Ordinal))
    {
        throw new BackendValidationException("Startup session payload does not match the authenticated desktop session.");
    }

    var account = accounts.RequireAccount(session.UserId, session.Email);
    return Results.Ok(accounts.BuildStartupSnapshot("session_check", account));
}).RequireRateLimiting("auth");

app.MapPost("/api/desktop/account/startup-check/callback", (
    HttpContext httpContext,
    AuthCallbackResultDto request,
    DesktopSessionService desktopSessions,
    AccountStateService accounts) =>
{
    var session = desktopSessions.RequireSession(httpContext.Request.Headers.Authorization.ToString());
    var account = accounts.RequireAccountForCallback(request);
    if (!string.Equals(account.UserId, session.UserId, StringComparison.Ordinal))
    {
        throw new BackendValidationException("Callback session does not match the authenticated desktop session.");
    }

    return Results.Ok(accounts.BuildStartupSnapshot("callback_check", account));
}).RequireRateLimiting("auth");

app.MapPost("/api/desktop/usage/reconcile", (
    HttpContext httpContext,
    UsageReconciliationRequestDto request,
    DesktopSessionService desktopSessions,
    UsageReconciliationService usage) =>
{
    var session = desktopSessions.RequireSession(httpContext.Request.Headers.Authorization.ToString());
    return Results.Ok(usage.Reconcile(request, session.UserId));
}).RequireRateLimiting("desktop-api");

app.MapPost("/api/desktop/telemetry/ingest", (
    HttpContext httpContext,
    TelemetryIngestRequestDto request,
    DesktopSessionService desktopSessions,
    TelemetryIngestService telemetry) =>
{
    desktopSessions.RequireSession(httpContext.Request.Headers.Authorization.ToString());
    var accepted = telemetry.Ingest(request);
    if (!accepted)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Telemetry buffer is full.");
    }

    return Results.Ok(new { accepted = true, queued = true });
}).RequireRateLimiting("telemetry");

app.MapGet("/api/desktop/ai/catalog", (
    HttpContext httpContext,
    ManagedAiService managedAi) =>
{
    var account = managedAi.RequireManagedAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(managedAi.GetCatalogForAccount(account));
}).RequireRateLimiting("desktop-api");

app.MapPost("/api/desktop/ai/chat", async (
    HttpContext httpContext,
    DesktopAiChatRequestDto request,
    ManagedAiService managedAi,
    CancellationToken cancellationToken) =>
{
    var account = managedAi.RequireManagedAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    await managedAi.StreamChatAsync(httpContext.Response, account, request, cancellationToken);
}).RequireRateLimiting("desktop-api");

app.MapGet("/api/desktop/kb", (
    HttpContext httpContext,
    HostedKnowledgeBaseService knowledgeBases) =>
{
    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(knowledgeBases.GetSummaryForAccount(account));
}).RequireRateLimiting("desktop-api");

app.MapGet("/api/desktop/kb/profile", (
    HttpContext httpContext,
    HostedKnowledgeBaseService knowledgeBases) =>
{
    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(knowledgeBases.GetProfileCard(account));
}).RequireRateLimiting("desktop-api");

app.MapPut("/api/desktop/kb/profile", async (
    HttpContext httpContext,
    HostedKnowledgeBaseProfileCardUpdateRequestDto request,
    HostedKnowledgeBaseService knowledgeBases,
    CancellationToken cancellationToken) =>
{
    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(await knowledgeBases.UpdateProfileCard(account, request, cancellationToken));
}).RequireRateLimiting("desktop-api");

app.MapGet("/api/desktop/kb/projects", (
    HttpContext httpContext,
    HostedKnowledgeBaseService knowledgeBases) =>
{
    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(knowledgeBases.ListProjectCards(account));
}).RequireRateLimiting("desktop-api");

app.MapGet("/api/desktop/kb/projects/{projectCardId}", (
    HttpContext httpContext,
    string projectCardId,
    HostedKnowledgeBaseService knowledgeBases) =>
{
    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(knowledgeBases.GetProjectCard(account, projectCardId));
}).RequireRateLimiting("desktop-api");

app.MapPut("/api/desktop/kb/projects/{projectCardId}", async (
    HttpContext httpContext,
    string projectCardId,
    HostedKnowledgeBaseProjectCardUpdateRequestDto request,
    HostedKnowledgeBaseService knowledgeBases,
    CancellationToken cancellationToken) =>
{
    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(await knowledgeBases.UpdateProjectCard(account, projectCardId, request, cancellationToken));
}).RequireRateLimiting("desktop-api");

app.MapPost("/api/desktop/kb/projects/{projectCardId}/recent", (
    HttpContext httpContext,
    string projectCardId,
    HostedKnowledgeBaseService knowledgeBases) =>
{
    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(knowledgeBases.SetRecentProject(account, projectCardId));
}).RequireRateLimiting("desktop-api");

app.MapGet("/api/desktop/support/tickets", (
    HttpContext httpContext,
    int? page,
    int? pageSize,
    DesktopSessionService desktopSessions,
    SupportTicketService supportTickets) =>
{
    var account = desktopSessions.RequireAccount(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(supportTickets.ListUserTickets(account.UserId, page ?? 1, pageSize ?? 10));
}).RequireRateLimiting("desktop-api");

app.MapPost("/api/desktop/support/tickets", (
    HttpContext httpContext,
    SupportTicketCreateRequestDto request,
    DesktopSessionService desktopSessions,
    SupportTicketService supportTickets) =>
{
    var account = desktopSessions.RequireAccount(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(supportTickets.CreateTicket(account, request));
}).RequireRateLimiting("desktop-api");

app.MapPut("/api/desktop/interview-question-banks/{sessionId}", (
    HttpContext httpContext,
    string sessionId,
    InterviewQuestionBankUpdateRequestDto request,
    DesktopSessionService desktopSessions,
    InterviewQuestionBankService questionBanks) =>
{
    var account = desktopSessions.RequireAccount(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(questionBanks.Update(account.UserId, sessionId, request));
}).RequireRateLimiting("desktop-api");

app.MapPost("/api/desktop/kb", (
    HttpContext httpContext,
    HostedKnowledgeBaseCreateRequestDto request,
    HostedKnowledgeBaseService knowledgeBases) =>
{
    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(knowledgeBases.CreateOrUpdateKnowledgeBase(account, request));
}).RequireRateLimiting("desktop-api");

app.MapPost("/api/desktop/kb/documents", async (
    HttpContext httpContext,
    HostedKnowledgeBaseService knowledgeBases,
    CancellationToken cancellationToken) =>
{
    if (!httpContext.Request.HasFormContentType)
    {
        throw new BackendValidationException("Knowledge-base uploads must use multipart/form-data.");
    }

    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    var form = await httpContext.Request.ReadFormAsync(cancellationToken);
    return Results.Ok(await knowledgeBases.UploadDocumentsAsync(
        account,
        form.Files,
        form["section"].FirstOrDefault() ?? string.Empty,
        cancellationToken));
}).RequireRateLimiting("desktop-api");

app.MapPost("/api/desktop/kb/paste", async (
    HttpContext httpContext,
    HostedKnowledgeBaseDocumentPasteRequestDto request,
    HostedKnowledgeBaseService knowledgeBases,
    CancellationToken cancellationToken) =>
{
    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(await knowledgeBases.PasteDocumentAsync(account, request, cancellationToken));
}).RequireRateLimiting("desktop-api");

app.MapGet("/api/desktop/kb/documents/{documentId}", (
    HttpContext httpContext,
    string documentId,
    HostedKnowledgeBaseService knowledgeBases) =>
{
    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(knowledgeBases.GetDocumentContent(account, documentId));
}).RequireRateLimiting("desktop-api");

app.MapDelete("/api/desktop/kb/documents/{documentId}", (
    HttpContext httpContext,
    string documentId,
    HostedKnowledgeBaseService knowledgeBases) =>
{
    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(knowledgeBases.DeleteDocument(account, documentId));
}).RequireRateLimiting("desktop-api");

app.MapPost("/api/desktop/kb/reindex", (
    HttpContext httpContext,
    HostedKnowledgeBaseService knowledgeBases) =>
{
    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(knowledgeBases.QueueReindex(account));
}).RequireRateLimiting("desktop-api");

app.MapGet("/api/desktop/kb/reindex", (
    HttpContext httpContext,
    string? jobId,
    HostedKnowledgeBaseService knowledgeBases) =>
{
    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(knowledgeBases.GetLatestReindexJob(account, jobId));
}).RequireRateLimiting("desktop-api");

app.MapGet("/api/desktop/kb/search", async (
    HttpContext httpContext,
    string query,
    string? preferredDocumentIds,
    int? maxSnippets,
    HostedKnowledgeBaseService knowledgeBases,
    CancellationToken cancellationToken) =>
{
    var account = knowledgeBases.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    var preferredDocs = string.IsNullOrWhiteSpace(preferredDocumentIds)
        ? Array.Empty<string>()
        : preferredDocumentIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
    return Results.Ok(await knowledgeBases.SearchAsync(account, query, preferredDocs, maxSnippets ?? 3, cancellationToken));
}).RequireRateLimiting("desktop-api");

app.MapGet("/api/desktop/context-packs", (
    HttpContext httpContext,
    DesktopContextPackService contextPacks) =>
{
    try
    {
        var account = contextPacks.RequirePremiumAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
        return Results.Ok(contextPacks.List(account));
    }
    catch (BackendValidationException validationException)
    {
        return Results.BadRequest(new { error = validationException.Message });
    }
}).RequireRateLimiting("desktop-api");

app.MapPost("/api/desktop/context-packs", (
    HttpContext httpContext,
    DesktopContextPackUpsertRequestDto request,
    DesktopContextPackService contextPacks) =>
{
    try
    {
        var account = contextPacks.RequirePremiumAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
        return Results.Ok(contextPacks.Upsert(account, request));
    }
    catch (BackendValidationException validationException)
    {
        return Results.BadRequest(new { error = validationException.Message });
    }
}).RequireRateLimiting("desktop-api");

app.MapPost("/api/desktop/context-packs/delete", (
    HttpContext httpContext,
    DesktopContextPackDeleteRequestDto request,
    DesktopContextPackService contextPacks) =>
{
    try
    {
        var account = contextPacks.RequirePremiumAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
        contextPacks.Delete(account, request.PackId);
        return Results.Ok(new { deleted = true });
    }
    catch (BackendValidationException validationException)
    {
        return Results.BadRequest(new { error = validationException.Message });
    }
}).RequireRateLimiting("desktop-api");

app.MapGet("/api/desktop/payments/catalog", (
    HttpContext httpContext,
    HostedKnowledgeBaseService access,
    PaymentService payments) =>
{
    var account = access.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(payments.GetCatalog(account));
}).RequireRateLimiting("payments");

app.MapGet("/api/desktop/payments/orders", (
    HttpContext httpContext,
    int? limit,
    HostedKnowledgeBaseService access,
    PaymentService payments) =>
{
    var account = access.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(payments.ListOrdersForUser(account.UserId, limit ?? 20));
}).RequireRateLimiting("payments");

app.MapPost("/api/desktop/payments/checkout", async (
    HttpContext httpContext,
    PaymentCheckoutCreateRequestDto request,
    HostedKnowledgeBaseService access,
    PaymentService payments,
    CancellationToken cancellationToken) =>
{
    var account = access.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(await payments.CreateCheckoutAsync(account, request, cancellationToken));
}).RequireRateLimiting("payments");

app.MapPost("/api/desktop/payments/client-confirm", (
    HttpContext httpContext,
    PaymentClientConfirmationRequestDto request,
    HostedKnowledgeBaseService access,
    PaymentService payments) =>
{
    var account = access.RequireAccountFromAccessToken(ResolveUserAuthorization(httpContext.Request));
    return Results.Ok(payments.ConfirmClientPayment(account, request));
}).RequireRateLimiting("payments");

app.MapPost("/api/payments/razorpay/webhook", async (
    HttpContext httpContext,
    PaymentService payments,
    CancellationToken cancellationToken) =>
{
    using var reader = new StreamReader(httpContext.Request.Body);
    var payload = await reader.ReadToEndAsync(cancellationToken);
    var signature = httpContext.Request.Headers["X-Razorpay-Signature"].ToString();
    return Results.Ok(await payments.ProcessWebhookAsync(payload, signature, cancellationToken));
}).RequireRateLimiting("payments");

app.MapPost("/api/desktop/locks/acquire", (
    HttpContext httpContext,
    DeviceLockAcquireRequestDto request,
    DesktopSessionService desktopSessions,
    LockService locks) =>
{
    var session = desktopSessions.RequireSession(httpContext.Request.Headers.Authorization.ToString());
    return Results.Ok(locks.Acquire(request, session));
}).RequireRateLimiting("desktop-api");

app.MapPost("/api/desktop/locks/heartbeat", (
    HttpContext httpContext,
    DeviceLockHeartbeatRequestDto request,
    DesktopSessionService desktopSessions,
    LockService locks) =>
{
    var session = desktopSessions.RequireSession(httpContext.Request.Headers.Authorization.ToString());
    return Results.Ok(locks.Heartbeat(request, session));
}).RequireRateLimiting("desktop-api");

app.MapPost("/api/desktop/locks/release", (
    HttpContext httpContext,
    DeviceLockReleaseRequestDto request,
    DesktopSessionService desktopSessions,
    LockService locks) =>
{
    var session = desktopSessions.RequireSession(httpContext.Request.Headers.Authorization.ToString());
    return Results.Ok(locks.Release(request, session));
}).RequireRateLimiting("desktop-api");

internalGroup.MapGet("/session/user", (
    HttpContext httpContext,
    DesktopSessionService desktopSessions) =>
{
    try
    {
        var session = desktopSessions.RequireSession(httpContext.Request.Headers.Authorization.ToString());
        return Results.Ok(new
        {
            session.UserId,
            session.Email,
            session.DeviceInstallId,
            session.DeviceFingerprintHash
        });
    }
    catch (BackendValidationException)
    {
        return Results.Unauthorized();
    }
});

internalGroup.MapGet("/session/admin", (
    HttpContext httpContext,
    AdminAuthService adminAuth) =>
{
    try
    {
        var admin = adminAuth.RequireAdminFromAuthorization(httpContext.Request.Headers.Authorization.ToString());
        return Results.Ok(new
        {
            isValid = true,
            admin.AdminId,
            admin.Email,
            admin.DisplayName,
            admin.Role
        });
    }
    catch (BackendValidationException)
    {
        return Results.Unauthorized();
    }
});

var adminGroup = app.MapGroup("/api/admin")
    .AddEndpointFilter<AdminApiKeyFilter>()
    .RequireRateLimiting("admin");

adminGroup.MapGet("/accounts/{userId}", (
    string userId,
    AdminService admin) =>
{
    return Results.Ok(admin.GetAccountSnapshot(userId));
});

adminGroup.MapGet("/accounts", (string? query, int? page, int? pageSize, AdminService admin) =>
{
    return Results.Ok(admin.ListAccounts(query ?? string.Empty, page ?? 1, pageSize ?? 20));
});

adminGroup.MapGet("/accounts/{userId}/ledger", (
    string userId,
    int? page,
    int? pageSize,
    AdminService admin) =>
{
    return Results.Ok(admin.GetLedgerEntries(userId, page ?? 1, pageSize ?? 10));
});

adminGroup.MapPost("/accounts/update", (
    AdminAccountUpdateRequestDto request,
    AdminService admin) =>
{
    return Results.Ok(admin.UpdateAccount(request));
});

adminGroup.MapGet("/overview", (AdminService admin) =>
{
    return Results.Ok(admin.GetOverview());
});

adminGroup.MapGet("/payments/orders", (int? page, int? pageSize, AdminService admin) =>
{
    return Results.Ok(admin.GetPaymentOrders(page ?? 1, pageSize ?? 20));
});

adminGroup.MapGet("/payments/webhooks", (int? page, int? pageSize, AdminService admin) =>
{
    return Results.Ok(admin.GetPaymentWebhookEvents(page ?? 1, pageSize ?? 20));
});

adminGroup.MapGet("/support/tickets", (
    string? query,
    string? status,
    int? page,
    int? pageSize,
    SupportTicketService supportTickets) =>
{
    return Results.Ok(supportTickets.ListAdminTickets(query ?? string.Empty, status ?? string.Empty, page ?? 1, pageSize ?? 20));
});

adminGroup.MapPost("/support/tickets/update", (
    SupportTicketUpdateRequestDto request,
    SupportTicketService supportTickets) =>
{
    return Results.Ok(supportTickets.UpdateTicket(request));
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

adminGroup.MapPost("/managed-ai/catalog/refresh", async (
    ManagedAiCatalogService catalogService,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await catalogService.RefreshConfiguredProvidersAsync(cancellationToken));
});

adminGroup.MapGet("/managed-ai/catalog", (ManagedAiCatalogService catalogService) =>
{
    return Results.Ok(new
    {
        providers = catalogService.ListCatalogProviders()
    });
});

adminGroup.MapGet("/managed-ai/selection", (ManagedAiCatalogService catalogService) =>
{
    return Results.Ok(catalogService.GetAdminRuntimeSelection());
});

adminGroup.MapPost("/managed-ai/selection", (
    ManagedAiRuntimeSelectionUpdateRequestDto request,
    ManagedAiCatalogService catalogService) =>
{
    return Results.Ok(catalogService.UpdateAdminRuntimeSelection(request));
});

adminGroup.MapPost("/managed-ai/catalog/vision", (
    ManagedAiModelVisionUpdateRequestDto request,
    ManagedAiCatalogService catalogService) =>
{
    return Results.Ok(catalogService.UpdateModelVisionSupport(request));
});

adminGroup.MapPost("/managed-ai/test", async (
    AdminManagedAiTestRequestDto request,
    ManagedAiDiagnosticsService diagnostics,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await diagnostics.RunInteractiveTestAsync(request, cancellationToken));
});

adminGroup.MapPost("/managed-ai/latency/check", (
    ManagedAiDiagnosticsService diagnostics) =>
{
    return Results.Ok(diagnostics.EnqueueLatencyCheck());
});

adminGroup.MapGet("/managed-ai/latency/status", (
    ManagedAiDiagnosticsService diagnostics) =>
{
    return Results.Ok(diagnostics.GetLatencyStatus());
});

adminGroup.MapGet("/kb/embedding-config", (IKnowledgeBaseEmbeddingService embeddingService) =>
{
    return Results.Ok(embeddingService.GetAdminConfiguration());
});

adminGroup.MapPost("/kb/embedding-config", (
    HostedKnowledgeBaseEmbeddingConfigUpdateRequestDto request,
    IKnowledgeBaseEmbeddingService embeddingService) =>
{
    return Results.Ok(embeddingService.UpdateAdminConfiguration(request));
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
    return Results.Redirect($"{redirectBase}/admin?gmail_oauth=success");
});

static bool IsBrowserRequest(HttpContext httpContext) =>
    !string.IsNullOrWhiteSpace(httpContext.Request.Headers.Origin);

static object SanitizeUserSession(AuthSessionDto session) => new
{
    session.UserId,
    session.Email,
    session.AccessToken,
    session.RefreshToken,
    session.AuthMethod,
    session.DeviceInstallId,
    session.DeviceFingerprintHash,
    session.AuthenticatedAtUtc,
    session.ExpiresAtUtc,
    session.IsAuthenticated
};

static object SanitizeAdminSession(AdminAuthSessionDto session) => new
{
    session.AdminId,
    session.Email,
    session.DisplayName,
    session.Role,
    session.AccessToken,
    session.RefreshToken,
    session.AuthMethod,
    session.AuthenticatedAtUtc,
    session.ExpiresAtUtc,
    session.IsAuthenticated
};

static string ResolveRefreshToken(string bodyToken, string? cookieToken) =>
    string.IsNullOrWhiteSpace(bodyToken) ? cookieToken ?? string.Empty : bodyToken;

static string ResolveUserAuthorization(HttpRequest request) =>
    RequestTokenResolver.GetAuthorizationHeader(
        request,
        request.Cookies.TryGetValue(BrowserSessionCookieService.UserAccessCookie, out var cookieToken) ? cookieToken : null);

app.Run();

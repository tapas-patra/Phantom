using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text.Json;
using Npgsql;
using Phantom.Dashboard.Backend.Infrastructure;
using Phantom.Dashboard.Backend.Persistence;
using Phantom.Dashboard.Backend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
});
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("Npgsql", LogLevel.Warning);
builder.Services.Configure<Microsoft.Extensions.Logging.Console.ConsoleLoggerOptions>(options =>
{
    options.MaxQueueLength = 2048;
    options.QueueFullMode = Microsoft.Extensions.Logging.Console.ConsoleLoggerQueueFullMode.DropWrite;
});

var options = DashboardOptions.FromConfiguration(builder.Configuration);
if (builder.Environment.IsProduction())
{
    options.ValidateForProduction();
}
builder.Services.AddSingleton(options);
if (options.TrustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
    {
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        forwarded.ForwardLimit = 1;
        forwarded.KnownNetworks.Clear();
        forwarded.KnownProxies.Clear();
    });
}
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<PostgresDashboardStore>();
builder.Services.AddSingleton<AuthorityBackendClient>();
builder.Services.AddSingleton<DashboardQueryService>();
builder.Services.AddSingleton<ManagedAiAdminService>();
builder.Services.AddSingleton<AdminSessionValidator>();
builder.Services.AddSingleton<UserSessionValidator>();
builder.Services.AddSingleton<BrowserSessionCookieService>();
builder.Services.AddSingleton<AdminApiKeyFilter>();
builder.Services.AddCors(cors =>
{
    cors.AddPolicy("dashboard", policy =>
    {
        var origins = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.PublicWebsiteBaseUrl))
        {
            origins.Add(options.PublicWebsiteBaseUrl);
        }

        origins.Add(DashboardOptions.DefaultPublicWebsiteBaseUrl);
        if (builder.Environment.IsDevelopment())
        {
            origins.Add("http://localhost:4173");
            origins.Add("https://localhost:4173");
        }

        policy.WithOrigins(origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rateLimiterOptions.OnRejected = (context, _) =>
    {
        var request = context.HttpContext.Request;
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Phantom.RateLimit");
        logger.LogWarning(
            "request_rate_limited service={Service} component={Component} event={Event} correlation_id={CorrelationId} operation_id={OperationId} method={Method} route_template={RouteTemplate} status_class={StatusClass} error_code={ErrorCode} outcome={Outcome}",
            "phantom-dashboard-backend", "http", "request_rate_limited",
            request.Headers["X-Phantom-Correlation-Id"].FirstOrDefault() ?? string.Empty,
            request.Headers["X-Phantom-Operation-Id"].FirstOrDefault() ?? string.Empty,
            request.Method,
            (context.HttpContext.GetEndpoint() as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText ?? "unmatched",
            "4xx", "rate_limited", "error");
        return ValueTask.CompletedTask;
    };
    rateLimiterOptions.AddPolicy("dashboard-user", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    rateLimiterOptions.AddPolicy("dashboard-admin", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    rateLimiterOptions.AddPolicy("dashboard-internal", _ =>
        RateLimitPartition.GetFixedWindowLimiter(
            "health",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

if (options.TrustForwardedHeaders)
{
    app.UseForwardedHeaders();
}
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseRouting();
app.Use(async (context, next) =>
{
    var incomingCorrelation = context.Request.Headers["X-Phantom-Correlation-Id"].FirstOrDefault();
    var incomingOperation = context.Request.Headers["X-Phantom-Operation-Id"].FirstOrDefault();
    var correlationId = IsValidOpaqueId(incomingCorrelation) ? incomingCorrelation! : Guid.NewGuid().ToString("N");
    var operationId = IsValidOpaqueId(incomingOperation) ? incomingOperation! : Guid.NewGuid().ToString("N");
    context.Request.Headers["X-Phantom-Correlation-Id"] = correlationId;
    context.Request.Headers["X-Phantom-Operation-Id"] = operationId;
    context.Response.Headers["X-Phantom-Correlation-Id"] = correlationId;
    context.Response.Headers["X-Phantom-Operation-Id"] = operationId;
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Phantom.Request");
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    var routineHealth = string.Equals(context.Request.Path.Value, "/health", StringComparison.OrdinalIgnoreCase);
    var routeTemplate = (context.GetEndpoint() as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
    Exception? requestError = null;
    using (logger.BeginScope(new Dictionary<string, object>
    {
        ["correlation_id"] = correlationId,
        ["operation_id"] = operationId,
        ["service"] = "phantom-dashboard-backend"
    }))
    {
        if (!routineHealth)
        {
            logger.LogInformation(
                "request_started service={Service} component={Component} event={Event} correlation_id={CorrelationId} operation_id={OperationId} method={Method}",
                "phantom-dashboard-backend", "http", "request_started", correlationId, operationId, context.Request.Method);
        }
        try { await next(); }
        catch (Exception ex)
        {
            requestError = ex;
            logger.LogError(
                "request_failed service={Service} component={Component} event={Event} correlation_id={CorrelationId} operation_id={OperationId} method={Method} error_code={ErrorCode} elapsed_ms={ElapsedMs}",
                "phantom-dashboard-backend", "http", "request_failed", correlationId, operationId,
                context.Request.Method, ex.GetType().Name, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        finally
        {
            var statusCode = requestError == null ? context.Response.StatusCode : StatusCodes.Status500InternalServerError;
            if (!routineHealth || requestError != null || context.Response.StatusCode >= 400)
            {
                logger.LogInformation(
                    "request_completed service={Service} component={Component} event={Event} correlation_id={CorrelationId} operation_id={OperationId} method={Method} route_template={RouteTemplate} status_class={StatusClass} elapsed_ms={ElapsedMs} outcome={Outcome}",
                    "phantom-dashboard-backend", "http", "request_completed", correlationId, operationId,
                    context.Request.Method, routeTemplate, $"{statusCode / 100}xx",
                    System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, statusCode < 400 ? "success" : "error");
            }
        }
    }
});

var lifecycleLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Phantom.Lifecycle");
app.Lifetime.ApplicationStarted.Register(() => lifecycleLogger.LogInformation(
    "service_started service={Service} component={Component} event={Event} environment={Environment} database_configured={DatabaseConfigured} authority_configured={AuthorityConfigured}",
    "phantom-dashboard-backend", "lifecycle", "service_started", app.Environment.EnvironmentName,
    !string.IsNullOrWhiteSpace(options.DatabaseUrl), options.HasWindowsBackendInternalAccess));
app.Lifetime.ApplicationStopping.Register(() => lifecycleLogger.LogInformation(
    "service_stopping service={Service} component={Component} event={Event}",
    "phantom-dashboard-backend", "lifecycle", "service_stopping"));

app.UseCors("dashboard");
app.Use(async (context, next) =>
{
    var isUnsafeMethod = !HttpMethods.IsGet(context.Request.Method)
        && !HttpMethods.IsHead(context.Request.Method)
        && !HttpMethods.IsOptions(context.Request.Method);
    if (isUnsafeMethod
        && context.Request.Path.StartsWithSegments("/api")
        && !string.IsNullOrWhiteSpace(context.Request.Headers.Origin)
        && !string.Equals(context.Request.Headers["X-Phantom-CSRF"].FirstOrDefault(), "1", StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "Browser request verification failed." });
        return;
    }

    await next();
});
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
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Phantom.Error");
        if (exception is UnauthorizedAccessException unauthorized)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            LogHandledError(logger, context, "4xx", "unauthorized");
            await context.Response.WriteAsJsonAsync(new { error = unauthorized.Message });
            return;
        }

        if (exception is InvalidOperationException invalidOperation)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            LogHandledError(logger, context, "4xx", "validation_failed");
            await context.Response.WriteAsJsonAsync(new { error = invalidOperation.Message });
            return;
        }

        if (exception is NpgsqlException || exception is SocketException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            LogHandledError(logger, context, "5xx", "database_unavailable", error: true);
            await context.Response.WriteAsJsonAsync(new { error = "Database unavailable." });
            return;
        }

        if (exception is HttpRequestException || exception is TaskCanceledException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            LogHandledError(logger, context, "5xx", "authority_unavailable", error: true);
            await context.Response.WriteAsJsonAsync(new { error = "Authority backend unavailable." });
            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        LogHandledError(logger, context, "5xx", exception?.GetType().Name ?? "unhandled_error", error: true);
        await context.Response.WriteAsJsonAsync(new { error = "An unexpected server error occurred." });
    });
});
app.UseRateLimiter();

app.MapGet("/health", (PostgresDashboardStore store) => Results.Ok(new
{
    status = "ok",
    service = "phantom-dashboard-backend",
    utc = DateTime.UtcNow
}));

app.MapGet("/health/details", (
    PostgresDashboardStore store,
    AuthorityBackendClient authority) => Results.Ok(new
{
    status = store.CanConnect() ? "ok" : "degraded",
    service = "phantom-dashboard-backend",
    utc = DateTime.UtcNow
})).RequireRateLimiting("dashboard-internal");

app.MapGet("/health/ready", (
    PostgresDashboardStore store,
    AuthorityBackendClient authority) =>
{
    if (!store.CanConnect())
    {
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Database unavailable");
    }

    if (!authority.IsReady())
    {
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Authority backend unavailable");
    }

    return Results.Ok(new { status = "ready", utc = DateTime.UtcNow });
}).RequireRateLimiting("dashboard-internal");

app.MapGet("/api/dashboard/account-summary", async (
    HttpContext httpContext,
    UserSessionValidator sessions,
    BrowserSessionCookieService cookies,
    DashboardQueryService queries,
    CancellationToken cancellationToken) =>
{
    var session = await sessions.RequireUserSessionAsync(
        cookies.GetUserAuthorizationHeader(httpContext.Request),
        cancellationToken);
    var summary = queries.GetAccountSummary(session.UserId, session.Email);
    return summary == null ? Results.NotFound() : Results.Ok(summary);
}).RequireRateLimiting("dashboard-user");

app.MapGet("/api/dashboard/wallet-history", async (
    HttpContext httpContext,
    int? page,
    int? pageSize,
    UserSessionValidator sessions,
    BrowserSessionCookieService cookies,
    DashboardQueryService queries,
    CancellationToken cancellationToken) =>
    Results.Ok(queries.GetWalletHistory(
        (await sessions.RequireUserSessionAsync(
            cookies.GetUserAuthorizationHeader(httpContext.Request),
            cancellationToken)).UserId,
        page ?? 1,
        pageSize ?? 12)))
    .RequireRateLimiting("dashboard-user");

app.MapGet("/api/dashboard/wallet-purchases", async (
    HttpContext httpContext,
    int? page,
    int? pageSize,
    UserSessionValidator sessions,
    BrowserSessionCookieService cookies,
    DashboardQueryService queries,
    CancellationToken cancellationToken) =>
    Results.Ok(queries.GetWalletPurchases(
        (await sessions.RequireUserSessionAsync(
            cookies.GetUserAuthorizationHeader(httpContext.Request),
            cancellationToken)).UserId,
        page ?? 1,
        pageSize ?? 12)))
    .RequireRateLimiting("dashboard-user");

app.MapGet("/api/dashboard/devices", async (
    HttpContext httpContext,
    int? page,
    int? pageSize,
    UserSessionValidator sessions,
    BrowserSessionCookieService cookies,
    DashboardQueryService queries,
    CancellationToken cancellationToken) =>
    Results.Ok(queries.GetDevices(
        (await sessions.RequireUserSessionAsync(
            cookies.GetUserAuthorizationHeader(httpContext.Request),
            cancellationToken)).UserId,
        page ?? 1,
        pageSize ?? 10)))
    .RequireRateLimiting("dashboard-user");

app.MapGet("/api/dashboard/download-entitlement", async (
    HttpContext httpContext,
    UserSessionValidator sessions,
    BrowserSessionCookieService cookies,
    DashboardQueryService queries,
    CancellationToken cancellationToken) =>
    Results.Ok(queries.GetDownloadEntitlement(
        (await sessions.RequireUserSessionAsync(
            cookies.GetUserAuthorizationHeader(httpContext.Request),
            cancellationToken)).UserId)))
    .RequireRateLimiting("dashboard-user");

app.MapGet("/api/dashboard/support/preview", async (
    HttpContext httpContext,
    UserSessionValidator sessions,
    BrowserSessionCookieService cookies,
    DashboardQueryService queries,
    CancellationToken cancellationToken) =>
    Results.Ok(queries.GetSupportPreview(
        (await sessions.RequireUserSessionAsync(
            cookies.GetUserAuthorizationHeader(httpContext.Request),
            cancellationToken)).UserId)))
    .RequireRateLimiting("dashboard-user");

app.MapGet("/api/dashboard/interview-question-banks", async (
    HttpContext httpContext,
    int? page,
    int? pageSize,
    UserSessionValidator sessions,
    BrowserSessionCookieService cookies,
    DashboardQueryService queries,
    CancellationToken cancellationToken) =>
    Results.Ok(queries.GetInterviewQuestionBanks(
        (await sessions.RequireUserSessionAsync(
            cookies.GetUserAuthorizationHeader(httpContext.Request),
            cancellationToken)).UserId,
        page ?? 1,
        pageSize ?? 10)))
    .RequireRateLimiting("dashboard-user");

var adminGroup = app.MapGroup("/api/dashboard/admin")
    .AddEndpointFilter<AdminApiKeyFilter>()
    .RequireRateLimiting("dashboard-admin");

adminGroup.MapGet("/overview", async (
    HttpContext httpContext,
    BrowserSessionCookieService cookies,
    ManagedAiAdminService managedAi,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await managedAi.GetOverview(
        cookies.GetAdminAuthorizationHeader(httpContext.Request),
        cancellationToken));
});
adminGroup.MapGet("/payments/orders", async (
    HttpContext httpContext,
    BrowserSessionCookieService cookies,
    int? limit,
    ManagedAiAdminService managedAi,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await managedAi.GetPaymentOrders(
        cookies.GetAdminAuthorizationHeader(httpContext.Request),
        limit ?? 100,
        cancellationToken));
});
adminGroup.MapGet("/payments/webhooks", async (
    HttpContext httpContext,
    BrowserSessionCookieService cookies,
    int? limit,
    ManagedAiAdminService managedAi,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await managedAi.GetPaymentWebhookEvents(
        cookies.GetAdminAuthorizationHeader(httpContext.Request),
        limit ?? 100,
        cancellationToken));
});
adminGroup.MapGet("/managed-ai/credentials", async (
    HttpContext httpContext,
    BrowserSessionCookieService cookies,
    ManagedAiAdminService managedAi,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await managedAi.GetCredentialInventory(
        cookies.GetAdminAuthorizationHeader(httpContext.Request),
        cancellationToken));
});
adminGroup.MapPost("/managed-ai/credentials", async (
    HttpContext httpContext,
    BrowserSessionCookieService cookies,
    JsonElement payload,
    ManagedAiAdminService managedAi,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await managedAi.UpsertCredential(
        cookies.GetAdminAuthorizationHeader(httpContext.Request),
        payload,
        cancellationToken));
});
adminGroup.MapPost("/managed-ai/catalog/refresh", async (
    HttpContext httpContext,
    BrowserSessionCookieService cookies,
    ManagedAiAdminService managedAi,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await managedAi.RefreshCatalog(
        cookies.GetAdminAuthorizationHeader(httpContext.Request),
        cancellationToken));
});
adminGroup.MapPost("/managed-ai/selection", async (
    HttpContext httpContext,
    BrowserSessionCookieService cookies,
    JsonElement payload,
    ManagedAiAdminService managedAi,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await managedAi.UpdateRuntimeSelection(
        cookies.GetAdminAuthorizationHeader(httpContext.Request),
        payload,
        cancellationToken));
});
adminGroup.MapPost("/kb/embedding-config", async (
    HttpContext httpContext,
    BrowserSessionCookieService cookies,
    JsonElement payload,
    ManagedAiAdminService managedAi,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await managedAi.UpdateKnowledgeBaseEmbeddingConfig(
        cookies.GetAdminAuthorizationHeader(httpContext.Request),
        payload,
        cancellationToken));
});
adminGroup.MapDelete("/managed-ai/credentials/{credentialId}", async (
    HttpContext httpContext,
    BrowserSessionCookieService cookies,
    string credentialId,
    ManagedAiAdminService managedAi,
    CancellationToken cancellationToken) =>
{
    await managedAi.DeleteCredential(
        cookies.GetAdminAuthorizationHeader(httpContext.Request),
        credentialId,
        cancellationToken);
    return Results.Ok(new { deleted = true, credentialId });
});

static bool IsValidOpaqueId(string? value) => value is { Length: >= 8 and <= 128 }
    && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

static void LogHandledError(ILogger logger, HttpContext context, string statusClass, string errorCode, bool error = false)
{
    const string template = "request_error_handled service={Service} component={Component} event={Event} correlation_id={CorrelationId} operation_id={OperationId} method={Method} status_class={StatusClass} error_code={ErrorCode} outcome={Outcome}";
    var values = new object?[]
    {
        "phantom-dashboard-backend", "http", "request_error_handled",
        context.Request.Headers["X-Phantom-Correlation-Id"].FirstOrDefault() ?? string.Empty,
        context.Request.Headers["X-Phantom-Operation-Id"].FirstOrDefault() ?? string.Empty,
        context.Request.Method, statusClass, errorCode, "error"
    };
    if (error) logger.LogError(template, values);
    else logger.LogWarning(template, values);
}

app.Run();

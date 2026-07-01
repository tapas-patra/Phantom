using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using Npgsql;
using Phantom.Dashboard.Backend.Infrastructure;
using Phantom.Dashboard.Backend.Persistence;
using Phantom.Dashboard.Backend.Services;

var builder = WebApplication.CreateBuilder(args);

var options = DashboardOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(options);
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
        origins.Add("http://localhost:4173");
        origins.Add("https://localhost:4173");

        policy.WithOrigins(origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors("dashboard");
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
        if (exception is UnauthorizedAccessException unauthorized)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = unauthorized.Message });
            return;
        }

        if (exception is InvalidOperationException invalidOperation)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = invalidOperation.Message });
            return;
        }

        if (exception is NpgsqlException || exception is SocketException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "Database unavailable." });
            return;
        }

        if (exception is HttpRequestException || exception is TaskCanceledException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "Authority backend unavailable." });
            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
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

app.Run();

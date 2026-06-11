using Phantom.Dashboard.Backend.Infrastructure;
using Phantom.Dashboard.Backend.Persistence;
using Phantom.Dashboard.Backend.Services;

var builder = WebApplication.CreateBuilder(args);

var options = DashboardOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<PostgresDashboardStore>();
builder.Services.AddSingleton<DashboardQueryService>();
builder.Services.AddSingleton<ManagedAiAdminService>();
builder.Services.AddSingleton<AdminApiKeyFilter>();
builder.Services.AddCors(cors =>
{
    cors.AddPolicy("dashboard", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors("dashboard");

app.MapGet("/health", (PostgresDashboardStore store) => Results.Ok(new
{
    status = "ok",
    database = store.CanConnect() ? "reachable" : "unreachable",
    service = "phantom-dashboard-backend",
    utc = DateTime.UtcNow
}));

app.MapGet("/api/dashboard/account-summary", (string? userId, string? email, DashboardQueryService queries) =>
{
    var summary = queries.GetAccountSummary(userId, email);
    return summary == null ? Results.NotFound() : Results.Ok(summary);
});

app.MapGet("/api/dashboard/wallet-history", (string userId, DashboardQueryService queries) =>
    Results.Ok(queries.GetWalletHistory(userId)));

app.MapGet("/api/dashboard/devices", (string userId, DashboardQueryService queries) =>
    Results.Ok(queries.GetDevices(userId)));

app.MapGet("/api/dashboard/download-entitlement", (string userId, DashboardQueryService queries) =>
    Results.Ok(queries.GetDownloadEntitlement(userId)));

app.MapGet("/api/dashboard/support/preview", (string userId, DashboardQueryService queries) =>
    Results.Ok(queries.GetSupportPreview(userId)));

var adminGroup = app.MapGroup("/api/dashboard/admin")
    .AddEndpointFilter<AdminApiKeyFilter>();

adminGroup.MapGet("/overview", (DashboardQueryService queries) => Results.Ok(queries.GetAdminOverview()));
adminGroup.MapGet("/managed-ai/credentials", async (
    ManagedAiAdminService managedAi,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await managedAi.GetCredentialInventory(cancellationToken));
});
adminGroup.MapPost("/managed-ai/credentials", async (
    JsonElement payload,
    ManagedAiAdminService managedAi,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await managedAi.UpsertCredential(payload, cancellationToken));
});
adminGroup.MapDelete("/managed-ai/credentials/{credentialId}", async (
    string credentialId,
    ManagedAiAdminService managedAi,
    CancellationToken cancellationToken) =>
{
    await managedAi.DeleteCredential(credentialId, cancellationToken);
    return Results.Ok(new { deleted = true, credentialId });
});

app.Run();

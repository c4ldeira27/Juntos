using System.Text.Json;
using System.Text.Json.Serialization;
using Agenda.Api.Auth;
using Agenda.Api.Data;
using Agenda.Api.GoogleApi;
using Agenda.Api.Groups;
using Agenda.Api.Infrastructure;
using Agenda.Api.Schedule;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- Data ----------
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default"), sql =>
        // Azure SQL has transient failures (failovers, throttling) and the free serverless tier
        // auto-pauses: the first query after a pause fails with 40613 while the database resumes.
        // EF retries those errors with exponential backoff instead of returning a 500.
        // Side effect: explicit transactions must run inside the execution strategy (see GroupEndpoints).
        sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(15), errorNumbersToAdd: null)));

var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("Agenda")
    .PersistKeysToDbContext<AppDbContext>();

// In Azure, the Data Protection keys are encrypted with a Key Vault key ("envelope encryption"):
// the database stores the keys wrapped, so a database leak alone can't decrypt the refresh tokens.
// The app reaches Key Vault with its managed identity (DefaultAzureCredential reads AZURE_CLIENT_ID): no secret in config.
if (builder.Configuration["DataProtection:KeyVaultKeyId"] is { Length: > 0 } keyVaultKeyId)
    dataProtection.ProtectKeysWithAzureKeyVault(new Uri(keyVaultKeyId), new DefaultAzureCredential());

// ---------- Google ----------
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GoogleTokenProtector>();
builder.Services.AddHttpClient<GoogleTokenService>();
builder.Services.AddScoped<GoogleClientFactory>();
builder.Services.AddScoped<AgendaService>();

// ---------- Auth ----------
builder.Services.AddGoogleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddAuthorization();

// ---------- HTTP ----------
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

// On Azure Container Apps, HTTPS terminates at the ingress and the app receives plain HTTP.
// Without reading X-Forwarded-Proto, ASP.NET would think it's on http and build
// the wrong Google redirect_uri (and reject the Secure cookie).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The container is only reachable through the ingress, so we trust it.
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
    app.UseHsts();

// In production the React build goes to wwwroot and is served by this same app:
// a single domain, no CORS, and the session cookie works with no extra setup.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapGroupEndpoints();

// Unknown /api routes must return 404, not React's index.html.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    // Dev always migrates on startup. In Azure it's opt-in (Database__MigrateOnStartup=true) and only safe
    // while there's a single replica: several replicas starting together would all try to migrate.
    // M5 moves this to a pipeline step (EF migration bundle) and turns the flag off.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.Run();

// Exposes the Program class to the integration tests (WebApplicationFactory<Program>).
public partial class Program;

using System.Text.Json;
using System.Text.Json.Serialization;
using Agenda.Api.Auth;
using Agenda.Api.Data;
using Agenda.Api.GoogleApi;
using Agenda.Api.Groups;
using Agenda.Api.Infrastructure;
using Agenda.Api.Schedule;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- Data ----------
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddDataProtection()
    .SetApplicationName("Agenda")
    .PersistKeysToDbContext<AppDbContext>();
// TODO(M4): .ProtectKeysWithAzureKeyVault(...) so the keys aren't stored in plain text in the database.

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

if (app.Environment.IsDevelopment())
{
    // In dev, migrations are applied on startup. In production this becomes a pipeline step (M4),
    // because several replicas starting together would all try to migrate at the same time.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.Run();

// Exposes the Program class to the integration tests (WebApplicationFactory<Program>).
public partial class Program;

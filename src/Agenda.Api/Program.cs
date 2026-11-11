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

// ---------- Dados ----------
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddDataProtection()
    .SetApplicationName("Agenda")
    .PersistKeysToDbContext<AppDbContext>();
// TODO(M4): .ProtectKeysWithAzureKeyVault(...) para as chaves não ficarem em texto puro no banco.

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

// No Azure Container Apps o HTTPS termina no ingress e o app recebe HTTP puro.
// Sem ler o X-Forwarded-Proto, o ASP.NET acharia que está em http e montaria
// o redirect_uri do Google errado (e recusaria o cookie Secure).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // O container só é alcançável pelo ingress, então confiamos nele.
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
    app.UseHsts();

// Em produção o build do React vai para wwwroot e é servido por este mesmo app:
// um domínio só, sem CORS, e o cookie de sessão funciona sem configuração extra.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapGroupEndpoints();

// Rotas /api desconhecidas devem dar 404, não devolver o index.html do React.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

if (app.Environment.IsDevelopment())
{
    // Em dev aplicamos as migrations ao subir. Em produção isso vira um passo do pipeline (M4),
    // porque com várias réplicas subindo juntas todas tentariam migrar ao mesmo tempo.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.Run();

// Expõe a classe Program para os testes de integração (WebApplicationFactory<Program>).
public partial class Program;

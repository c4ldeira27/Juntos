using System.Security.Claims;
using Agenda.Api.Data;
using Agenda.Api.GoogleApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.EntityFrameworkCore;

namespace Agenda.Api.Auth;

public static class AppClaims
{
    public const string UserId = "agenda:uid";

    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(UserId), out var id) ? id : null;
}

/// <summary>
/// Padrão BFF: o backend faz o OAuth com o Google e guarda os tokens.
/// O navegador só recebe um cookie HttpOnly de sessão (JavaScript não consegue ler, então XSS não rouba).
/// </summary>
public static class GoogleAuthentication
{
    public static IServiceCollection AddGoogleAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment env)
    {
        var clientId = configuration["Google:ClientId"];
        var clientSecret = configuration["Google:ClientSecret"];

        // Fail fast: sem as credenciais o app quebraria em toda requisição com um erro confuso.
        // (EF.IsDesignTime: o "dotnet ef migrations" também executa o Program, e lá não precisamos do Google.)
        if (!EF.IsDesignTime && (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)))
            throw new InvalidOperationException(
                "Google:ClientId e Google:ClientSecret não configurados. Veja docs/setup-google-cloud.md.");

        // Em dev rodamos em http://localhost; em produção o cookie só trafega em HTTPS.
        var securePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "agenda.session";
                o.Cookie.HttpOnly = true;
                // Lax: o navegador não envia o cookie em POST vindo de outro site, o que já barra CSRF nos endpoints de escrita.
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = securePolicy;
                o.ExpireTimeSpan = TimeSpan.FromDays(14);
                o.SlidingExpiration = true;

                // Para uma API, "não autenticado" é 401, não redirect para uma página de login.
                o.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                o.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddGoogle(o =>
            {
                o.ClientId = clientId ?? "";
                o.ClientSecret = clientSecret ?? "";

                // offline = o Google devolve um refresh token, que nos deixa ler a agenda
                // mesmo quando o usuário não está com o app aberto (necessário para o parceiro ver).
                o.AccessType = "offline";
                foreach (var scope in GoogleScopes.Requested)
                    o.Scope.Add(scope);

                // Não guardamos os tokens no cookie (deixaria o cookie enorme e exporia o token ao navegador).
                o.SaveTokens = false;
                o.ClaimActions.MapJsonKey("picture", "picture");

                // O cookie de correlação protege o callback contra CSRF. Lax basta porque o Google
                // volta para /signin-google com um GET de navegação normal.
                o.CorrelationCookie.SameSite = SameSiteMode.Lax;
                o.CorrelationCookie.SecurePolicy = securePolicy;

                o.Events.OnCreatingTicket = OnCreatingTicketAsync;
            });

        return services;
    }

    /// <summary>Roda uma vez por login bem-sucedido no Google: cria/atualiza o usuário e guarda o refresh token.</summary>
    private static async Task OnCreatingTicketAsync(OAuthCreatingTicketContext ctx)
    {
        var identity = ctx.Identity ?? throw new InvalidOperationException("Identity ausente no login do Google.");
        var subject = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? throw new InvalidOperationException("Google não retornou o 'sub' do usuário.");
        var email = identity.FindFirst(ClaimTypes.Email)?.Value ?? "";
        var name = identity.FindFirst(ClaimTypes.Name)?.Value ?? email;
        var picture = identity.FindFirst("picture")?.Value;

        var services = ctx.HttpContext.RequestServices;
        var db = services.GetRequiredService<AppDbContext>();
        var protector = services.GetRequiredService<GoogleTokenProtector>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var ct = ctx.HttpContext.RequestAborted;

        var user = await db.Users
            .Include(u => u.GoogleConnection)
            .SingleOrDefaultAsync(u => u.GoogleSubject == subject, ct);

        if (user is null)
        {
            user = new User
            {
                Id = Guid.CreateVersion7(),
                GoogleSubject = subject,
                Email = email,
                Name = name,
                CreatedAt = now,
            };
            db.Users.Add(user);
        }

        user.Email = email;
        user.Name = name;
        user.AvatarUrl = picture;
        user.LastLoginAt = now;

        // O Google só manda refresh token na PRIMEIRA autorização (ou quando forçamos prompt=consent).
        // Em logins seguintes ele vem nulo e mantemos o que já está no banco.
        if (!string.IsNullOrEmpty(ctx.RefreshToken))
        {
            var encrypted = protector.Protect(ctx.RefreshToken);
            var scopes = ReadGrantedScopes(ctx);

            if (user.GoogleConnection is null)
            {
                user.GoogleConnection = new GoogleConnection
                {
                    UserId = user.Id,
                    RefreshTokenEncrypted = encrypted,
                    GrantedScopes = scopes,
                    Status = GoogleConnectionStatus.Ok,
                    UpdatedAt = now,
                };
            }
            else
            {
                user.GoogleConnection.RefreshTokenEncrypted = encrypted;
                user.GoogleConnection.GrantedScopes = scopes;
                user.GoogleConnection.Status = GoogleConnectionStatus.Ok;
                user.GoogleConnection.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);

        // A partir daqui o app identifica o usuário pelo NOSSO id, não pelo do Google.
        identity.AddClaim(new Claim(AppClaims.UserId, user.Id.ToString()));
    }

    private static string ReadGrantedScopes(OAuthCreatingTicketContext ctx) =>
        ctx.TokenResponse.Response?.RootElement.TryGetProperty("scope", out var scope) == true
            ? scope.GetString() ?? ""
            : "";
}

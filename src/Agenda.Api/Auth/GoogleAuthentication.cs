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
/// BFF pattern: the back end runs the OAuth flow with Google and keeps the tokens.
/// The browser only gets an HttpOnly session cookie (JavaScript can't read it, so XSS can't steal it).
/// </summary>
public static class GoogleAuthentication
{
    public static IServiceCollection AddGoogleAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment env)
    {
        var clientId = configuration["Google:ClientId"];
        var clientSecret = configuration["Google:ClientSecret"];

        // Fail fast: without the credentials the app would break on every request with a confusing error.
        // (EF.IsDesignTime: "dotnet ef migrations" also runs Program, and Google isn't needed there.)
        if (!EF.IsDesignTime && (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)))
            throw new InvalidOperationException(
                "Google:ClientId and Google:ClientSecret are not configured. See docs/setup-google-cloud.md.");

        // Dev runs on http://localhost; in production the cookie only travels over HTTPS.
        var securePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "agenda.session";
                o.Cookie.HttpOnly = true;
                // Lax: the browser doesn't send the cookie on POSTs coming from other sites, which already blocks CSRF on write endpoints.
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = securePolicy;
                o.ExpireTimeSpan = TimeSpan.FromDays(14);
                o.SlidingExpiration = true;

                // For an API, "not authenticated" is a 401, not a redirect to a login page.
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

                // offline = Google returns a refresh token, which lets us read the calendar
                // even when the user doesn't have the app open (needed for the partner to see it).
                o.AccessType = "offline";
                foreach (var scope in GoogleScopes.Requested)
                    o.Scope.Add(scope);

                // Tokens are not stored in the cookie (it would get huge and expose the token to the browser).
                o.SaveTokens = false;
                o.ClaimActions.MapJsonKey("picture", "picture");

                // The correlation cookie protects the callback against CSRF. Lax is enough because Google
                // comes back to /signin-google with a regular top-level GET navigation.
                o.CorrelationCookie.SameSite = SameSiteMode.Lax;
                o.CorrelationCookie.SecurePolicy = securePolicy;

                o.Events.OnCreatingTicket = OnCreatingTicketAsync;
            });

        return services;
    }

    /// <summary>Runs once per successful Google login: creates/updates the user and stores the refresh token.</summary>
    private static async Task OnCreatingTicketAsync(OAuthCreatingTicketContext ctx)
    {
        var identity = ctx.Identity ?? throw new InvalidOperationException("Missing identity on Google login.");
        var subject = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? throw new InvalidOperationException("Google did not return the user's 'sub'.");
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

        // Google only sends a refresh token on the FIRST authorization (or when we force prompt=consent).
        // On later logins it is null and we keep the one already in the database.
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

        // From here on the app identifies the user by OUR id, not Google's.
        identity.AddClaim(new Claim(AppClaims.UserId, user.Id.ToString()));
    }

    private static string ReadGrantedScopes(OAuthCreatingTicketContext ctx) =>
        ctx.TokenResponse.Response?.RootElement.TryGetProperty("scope", out var scope) == true
            ? scope.GetString() ?? ""
            : "";
}

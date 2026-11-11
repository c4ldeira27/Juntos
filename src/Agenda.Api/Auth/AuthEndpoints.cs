using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;

namespace Agenda.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        // The front end navigates here (window.location), it doesn't fetch: the OAuth flow is a chain of redirects.
        group.MapGet("/login", (string? returnUrl, bool? consent) =>
        {
            var properties = new GoogleChallengeProperties
            {
                RedirectUri = SafeLocalUrl(returnUrl),
                // select_account: couples often have more than one Google account in the same browser.
                // consent: forces the permissions screen so Google issues a new refresh token
                // (used when the connection was revoked or the user unchecked the calendar permission).
                Prompt = consent == true ? "consent select_account" : "select_account",
            };
            return Results.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
        });

        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }

    /// <summary>
    /// Prevents "open redirect": otherwise someone could send a link like
    /// /auth/login?returnUrl=https://fake-site.com and use our login as a springboard for phishing.
    /// </summary>
    internal static string SafeLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\")
            ? url
            : "/";
}

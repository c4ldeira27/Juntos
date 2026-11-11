using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;

namespace Agenda.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        // O front navega para cá (window.location), não faz fetch: o fluxo OAuth é feito de redirects.
        group.MapGet("/login", (string? returnUrl, bool? consent) =>
        {
            var properties = new GoogleChallengeProperties
            {
                RedirectUri = SafeLocalUrl(returnUrl),
                // select_account: casais costumam ter mais de uma conta Google no mesmo navegador.
                // consent: força a tela de permissões para o Google emitir um novo refresh token
                // (usado quando a conexão foi revogada ou o usuário desmarcou a permissão da agenda).
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
    /// Evita "open redirect": sem isso, alguém poderia mandar um link
    /// /auth/login?returnUrl=https://site-falso.com e usar nosso login como trampolim para phishing.
    /// </summary>
    internal static string SafeLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\")
            ? url
            : "/";
}

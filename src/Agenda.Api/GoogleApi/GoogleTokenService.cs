using System.Text.Json;
using System.Text.Json.Serialization;
using Agenda.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Agenda.Api.GoogleApi;

public sealed class GoogleReconnectRequiredException()
    : Exception("A conexão com o Google expirou ou foi revogada. O usuário precisa autorizar novamente.");

/// <summary>
/// Troca o refresh token (guardado no banco) por um access token (dura ~1h, fica em cache).
/// Fazemos a chamada HTTP na mão em vez de usar o fluxo do SDK para ficar explícito o que acontece.
/// </summary>
public sealed class GoogleTokenService(
    HttpClient http,
    AppDbContext db,
    GoogleTokenProtector protector,
    IMemoryCache cache,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<GoogleTokenService> logger)
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private static string CacheKey(Guid userId) => $"google-access-token:{userId}";

    public void Invalidate(Guid userId) => cache.Remove(CacheKey(userId));

    public async Task<string> GetAccessTokenAsync(Guid userId, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey(userId), out string? cached) && cached is not null)
            return cached;

        var connection = await db.GoogleConnections.SingleOrDefaultAsync(c => c.UserId == userId, ct);
        if (connection is null || connection.Status != GoogleConnectionStatus.Ok)
            throw new GoogleReconnectRequiredException();

        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = configuration["Google:ClientId"]!,
            ["client_secret"] = configuration["Google:ClientSecret"]!,
            ["refresh_token"] = protector.Unprotect(connection.RefreshTokenEncrypted),
            ["grant_type"] = "refresh_token",
        }), ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await TryReadErrorAsync(response, ct);

            // invalid_grant = token revogado pelo usuário, senha trocada ou token expirado.
            // Não adianta tentar de novo: marcamos para o front pedir nova autorização.
            if (error == "invalid_grant")
            {
                logger.LogWarning("Refresh token do usuário {UserId} inválido; marcando para reconectar", userId);
                connection.Status = GoogleConnectionStatus.NeedsReconnect;
                connection.UpdatedAt = time.GetUtcNow();
                await db.SaveChangesAsync(ct);
                throw new GoogleReconnectRequiredException();
            }

            response.EnsureSuccessStatusCode();
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                    ?? throw new InvalidOperationException("Resposta vazia do endpoint de token do Google.");

        // Expira do cache 2 min antes do Google, para nunca usarmos um token prestes a vencer.
        cache.Set(CacheKey(userId), token.AccessToken, TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn - 120)));
        return token.AccessToken;
    }

    private static async Task<string?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<TokenErrorResponse>(ct);
            return body?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record TokenErrorResponse([property: JsonPropertyName("error")] string? Error);
}

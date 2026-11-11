using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Tasks.v1;

namespace Agenda.Api.GoogleApi;

public sealed record GoogleClients(CalendarService Calendar, TasksService Tasks) : IDisposable
{
    public void Dispose()
    {
        Calendar.Dispose();
        Tasks.Dispose();
    }
}

/// <summary>
/// Cria os clientes do SDK do Google já autenticados como o usuário.
/// O token é obtido UMA vez por requisição, antes das chamadas paralelas:
/// o DbContext não é thread-safe, então não podemos deixar cada chamada paralela buscar o token.
/// </summary>
public sealed class GoogleClientFactory(GoogleTokenService tokens)
{
    public async Task<GoogleClients> CreateAsync(Guid userId, CancellationToken ct)
    {
        var accessToken = await tokens.GetAccessTokenAsync(userId, ct);
        var initializer = new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
            ApplicationName = "Agenda",
        };

        return new GoogleClients(new CalendarService(initializer), new TasksService(initializer));
    }
}

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
/// Creates Google SDK clients already authenticated as the user.
/// The token is fetched ONCE per request, before the parallel calls:
/// DbContext is not thread-safe, so each parallel call can't fetch its own token.
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

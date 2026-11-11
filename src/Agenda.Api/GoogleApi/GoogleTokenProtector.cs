using Microsoft.AspNetCore.DataProtection;

namespace Agenda.Api.GoogleApi;

/// <summary>
/// Encrypts the refresh token before it goes to the database. If a database backup leaks,
/// the tokens are useless without the keys (which, in production, will be protected by Key Vault).
/// </summary>
public sealed class GoogleTokenProtector(IDataProtectionProvider provider)
{
    // The "purpose" isolates this key: a value protected here cannot be opened by another protector.
    private readonly IDataProtector _protector = provider.CreateProtector("Agenda.GoogleRefreshToken.v1");

    public string Protect(string refreshToken) => _protector.Protect(refreshToken);

    public string Unprotect(string encrypted) => _protector.Unprotect(encrypted);
}

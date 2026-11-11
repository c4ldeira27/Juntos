using Microsoft.AspNetCore.DataProtection;

namespace Agenda.Api.GoogleApi;

/// <summary>
/// Criptografa o refresh token antes de ir para o banco. Se alguém vazar um backup do banco,
/// não consegue usar os tokens sem as chaves (que em produção serão protegidas pelo Key Vault).
/// </summary>
public sealed class GoogleTokenProtector(IDataProtectionProvider provider)
{
    // O "purpose" isola esta chave: um valor protegido aqui não pode ser aberto por outro protector.
    private readonly IDataProtector _protector = provider.CreateProtector("Agenda.GoogleRefreshToken.v1");

    public string Protect(string refreshToken) => _protector.Protect(refreshToken);

    public string Unprotect(string encrypted) => _protector.Unprotect(encrypted);
}

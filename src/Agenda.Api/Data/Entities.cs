namespace Agenda.Api.Data;

public sealed class User
{
    public Guid Id { get; set; }

    /// <summary>"sub" do Google: identificador estável da conta (o e-mail pode mudar, o sub não).</summary>
    public required string GoogleSubject { get; set; }

    public required string Email { get; set; }
    public required string Name { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastLoginAt { get; set; }

    public GoogleConnection? GoogleConnection { get; set; }
}

public enum GoogleConnectionStatus
{
    Ok = 1,

    /// <summary>O refresh token foi revogado ou expirou: o usuário precisa autorizar de novo.</summary>
    NeedsReconnect = 2,
}

/// <summary>
/// Conexão com a conta Google. Guardamos só o refresh token (criptografado);
/// o access token dura ~1h e fica apenas em cache de memória.
/// </summary>
public sealed class GoogleConnection
{
    public Guid UserId { get; set; }
    public required string RefreshTokenEncrypted { get; set; }

    /// <summary>Escopos concedidos, separados por espaço (o usuário pode desmarcar algum na tela do Google).</summary>
    public required string GrantedScopes { get; set; }

    public GoogleConnectionStatus Status { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool HasScope(string scope) =>
        GrantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope);
}

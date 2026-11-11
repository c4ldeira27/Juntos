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

/// <summary>Grupo de pessoas que veem as agendas umas das outras (no caso de vocês, o casal).</summary>
public sealed class Group
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Só informativo, sem FK: evita múltiplos caminhos de cascade no SQL Server (User → Group → Member e User → Member).</summary>
    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<GroupMember> Members { get; set; } = [];
    public List<GroupInvite> Invites { get; set; } = [];
}

public enum GroupRole
{
    Owner = 1,
    Member = 2,
}

public sealed class GroupMember
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public GroupRole Role { get; set; }

    /// <summary>Cor da pessoa no grupo (hex). Todos os eventos dela aparecem nessa cor.</summary>
    public required string Color { get; set; }

    public DateTimeOffset JoinedAt { get; set; }

    /// <summary>Quando a pessoa salvou as escolhas de compartilhamento. Nulo = ainda não escolheu (o front abre o onboarding).</summary>
    public DateTimeOffset? SharingConfiguredAt { get; set; }

    public Group Group { get; set; } = null!;
    public User User { get; set; } = null!;
    public List<SharedSource> SharedSources { get; set; } = [];
}

/// <summary>
/// Convite por link. Guardamos o HASH do token, não o token: quem tiver acesso ao banco
/// não consegue montar um link válido (mesma lógica de senha).
/// </summary>
public sealed class GroupInvite
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public required string TokenHash { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public Guid? UsedByUserId { get; set; }

    public Group Group { get; set; } = null!;
}

/// <summary>
/// Uma agenda (ou lista de tarefas) que um membro decidiu mostrar no grupo, e com quanto detalhe.
/// Sem linha = não compartilhada. Pertence ao GroupMember: ao sair do grupo, o banco apaga em cascata.
/// </summary>
public sealed class SharedSource
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public Schedule.SourceKind Kind { get; set; }
    public required string GoogleId { get; set; }

    /// <summary>Nome no momento em que foi compartilhada (mostrado se a agenda sumir do Google).</summary>
    public required string DisplayName { get; set; }

    public Schedule.SourceVisibility Visibility { get; set; }
}

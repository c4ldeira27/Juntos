namespace Agenda.Api.Data;

public sealed class User
{
    public Guid Id { get; set; }

    /// <summary>Google "sub": the stable account identifier (the e-mail can change, the sub cannot).</summary>
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

    /// <summary>The refresh token was revoked or expired: the user must authorize again.</summary>
    NeedsReconnect = 2,
}

/// <summary>
/// Connection to the Google account. Only the refresh token is stored (encrypted);
/// the access token lasts ~1h and lives only in the memory cache.
/// </summary>
public sealed class GoogleConnection
{
    public Guid UserId { get; set; }
    public required string RefreshTokenEncrypted { get; set; }

    /// <summary>Granted scopes, space-separated (the user can uncheck some on Google's consent screen).</summary>
    public required string GrantedScopes { get; set; }

    public GoogleConnectionStatus Status { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool HasScope(string scope) =>
        GrantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope);
}

/// <summary>A group of people who see each other's calendars (in our case, the couple).</summary>
public sealed class Group
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Informational only, no FK: avoids multiple cascade paths in SQL Server (User → Group → Member and User → Member).</summary>
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

    /// <summary>The person's color in the group (hex). All of their events are shown in this color.</summary>
    public required string Color { get; set; }

    public DateTimeOffset JoinedAt { get; set; }

    /// <summary>When the person saved their sharing choices. Null = not chosen yet (the front end opens onboarding).</summary>
    public DateTimeOffset? SharingConfiguredAt { get; set; }

    public Group Group { get; set; } = null!;
    public User User { get; set; } = null!;
    public List<SharedSource> SharedSources { get; set; } = [];
}

/// <summary>
/// Invite link. We store the token's HASH, not the token itself: anyone with database access
/// still can't build a valid link (same idea as passwords).
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
/// A calendar (or task list) a member chose to show in the group, and with how much detail.
/// No row = not shared. Owned by the GroupMember: leaving the group cascades the delete in the database.
/// </summary>
public sealed class SharedSource
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public Schedule.SourceKind Kind { get; set; }
    public required string GoogleId { get; set; }

    /// <summary>Name at the time it was shared (shown if the calendar disappears from Google).</summary>
    public required string DisplayName { get; set; }

    public Schedule.SourceVisibility Visibility { get; set; }
}

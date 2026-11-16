using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Agenda.Api.Data;

namespace Agenda.Api.Groups;

public static class GroupRules
{
    public const int MaxMembers = 6;
    public const int MaxGroupsPerUser = 10;
    public const int MaxNameLength = 60;
    public const int MaxSharedSources = 30;
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromHours(48);

    /// <summary>One color per person, in the order they join the group (first violet, then coral...).</summary>
    public static readonly string[] MemberColors = ["#7c6cff", "#ff7a6b", "#14b8a6", "#f5a524", "#38bdf8", "#ec4899"];

    /// <summary>First palette color nobody in the group is using.</summary>
    public static string NextColor(IEnumerable<string> usedColors)
    {
        var used = usedColors.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return MemberColors.FirstOrDefault(c => !used.Contains(c)) ?? MemberColors[used.Count % MemberColors.Length];
    }

    // ---------------- Permissions ----------------
    // Kept here as pure functions so the whole permission matrix is in one place and unit-tested.

    /// <summary>Rename the group and create invites.</summary>
    public static bool CanManage(GroupRole actor) => actor is GroupRole.Owner or GroupRole.Admin;

    public static bool CanDelete(GroupRole actor) => actor == GroupRole.Owner;

    /// <summary>Promote a member to admin or demote an admin to member.</summary>
    public static bool CanChangeRoles(GroupRole actor) => actor == GroupRole.Owner;

    /// <summary>The owner can remove anyone else; an admin only regular members; members nobody.</summary>
    public static bool CanRemove(GroupRole actor, GroupRole target) => actor switch
    {
        GroupRole.Owner => target != GroupRole.Owner,
        GroupRole.Admin => target == GroupRole.Member,
        _ => false,
    };

    /// <summary>
    /// When the owner leaves, someone must take over: the longest-standing admin, otherwise the longest-standing member.
    /// </summary>
    public static GroupMember? NextOwner(IEnumerable<GroupMember> remaining) =>
        remaining
            .OrderBy(m => m.Role == GroupRole.Admin ? 0 : 1)
            .ThenBy(m => m.JoinedAt)
            .FirstOrDefault();
}

public static class InviteTokens
{
    /// <summary>
    /// 32 random bytes (256 bits) from a cryptographic generator: impossible to guess by brute force.
    /// Base64Url because the token goes in the URL (regular Base64 has "+" and "/").
    /// </summary>
    public static (string Token, string Hash) Create()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    /// <summary>
    /// Unsalted SHA-256 is enough here: salt exists to protect weak, human-chosen passwords.
    /// A 256-bit random token has no dictionary to attack.
    /// </summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Agenda.Api.Groups;

public static class GroupRules
{
    public const int MaxMembers = 6;
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

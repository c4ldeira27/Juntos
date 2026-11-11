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

    /// <summary>Uma cor por pessoa, na ordem em que entram no grupo (você violeta, ela coral...).</summary>
    public static readonly string[] MemberColors = ["#7c6cff", "#ff7a6b", "#14b8a6", "#f5a524", "#38bdf8", "#ec4899"];

    /// <summary>Primeira cor da paleta que ninguém do grupo está usando.</summary>
    public static string NextColor(IEnumerable<string> usedColors)
    {
        var used = usedColors.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return MemberColors.FirstOrDefault(c => !used.Contains(c)) ?? MemberColors[used.Count % MemberColors.Length];
    }
}

public static class InviteTokens
{
    /// <summary>
    /// 32 bytes aleatórios (256 bits) de um gerador criptográfico: impossível adivinhar por força bruta.
    /// Base64Url porque o token vai na URL (o Base64 comum tem "+" e "/").
    /// </summary>
    public static (string Token, string Hash) Create()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    /// <summary>
    /// SHA-256 sem salt é suficiente aqui: o salt existe para proteger senhas fracas, escolhidas por humanos.
    /// Um token de 256 bits aleatórios não tem dicionário para atacar.
    /// </summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

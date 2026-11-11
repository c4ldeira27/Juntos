using Agenda.Api.Groups;

namespace Agenda.Api.Tests;

public class GroupRulesTests
{
    [Fact]
    public void NextColor_FirstPerson_GetsFirstColor()
    {
        Assert.Equal(GroupRules.MemberColors[0], GroupRules.NextColor([]));
    }

    [Fact]
    public void NextColor_SkipsColorsInUse()
    {
        var used = new[] { GroupRules.MemberColors[0] };

        Assert.Equal(GroupRules.MemberColors[1], GroupRules.NextColor(used));
    }

    [Fact]
    public void NextColor_ReusesColorFreedBySomeoneWhoLeft()
    {
        // The person with color [0] left; only [1] remains. The next one to join gets [0].
        var used = new[] { GroupRules.MemberColors[1] };

        Assert.Equal(GroupRules.MemberColors[0], GroupRules.NextColor(used));
    }

    [Fact]
    public void NextColor_IgnoresCase()
    {
        var used = new[] { GroupRules.MemberColors[0].ToUpperInvariant() };

        Assert.Equal(GroupRules.MemberColors[1], GroupRules.NextColor(used));
    }
}

public class InviteTokensTests
{
    [Fact]
    public void Create_TokenIsUrlSafeWith256Bits()
    {
        var (token, _) = InviteTokens.Create();

        Assert.Equal(43, token.Length); // 32 bytes in Base64Url without padding
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
    }

    [Fact]
    public void Hash_IsDeterministicAndDifferentFromToken()
    {
        var (token, hash) = InviteTokens.Create();

        Assert.Equal(hash, InviteTokens.Hash(token));
        Assert.NotEqual(token, hash);
        Assert.Equal(64, hash.Length); // SHA-256 in hex
    }

    [Fact]
    public void Create_TokensDoNotRepeat()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => InviteTokens.Create().Token).ToHashSet();

        Assert.Equal(1000, tokens.Count);
    }
}

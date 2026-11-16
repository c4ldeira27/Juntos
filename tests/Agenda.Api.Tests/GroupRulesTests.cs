using Agenda.Api.Data;
using Agenda.Api.Groups;

namespace Agenda.Api.Tests;

public class GroupPermissionTests
{
    [Theory]
    [InlineData(GroupRole.Owner, true)]
    [InlineData(GroupRole.Admin, true)]
    [InlineData(GroupRole.Member, false)]
    public void CanManage_RenameAndInvite(GroupRole actor, bool expected) =>
        Assert.Equal(expected, GroupRules.CanManage(actor));

    [Theory]
    [InlineData(GroupRole.Owner, true)]
    [InlineData(GroupRole.Admin, false)]
    [InlineData(GroupRole.Member, false)]
    public void CanDelete_OnlyOwner(GroupRole actor, bool expected) =>
        Assert.Equal(expected, GroupRules.CanDelete(actor));

    [Theory]
    [InlineData(GroupRole.Owner, true)]
    [InlineData(GroupRole.Admin, false)]
    [InlineData(GroupRole.Member, false)]
    public void CanChangeRoles_OnlyOwner(GroupRole actor, bool expected) =>
        Assert.Equal(expected, GroupRules.CanChangeRoles(actor));

    [Theory]
    [InlineData(GroupRole.Owner, GroupRole.Admin, true)]
    [InlineData(GroupRole.Owner, GroupRole.Member, true)]
    [InlineData(GroupRole.Owner, GroupRole.Owner, false)]
    [InlineData(GroupRole.Admin, GroupRole.Member, true)]
    [InlineData(GroupRole.Admin, GroupRole.Admin, false)]
    [InlineData(GroupRole.Admin, GroupRole.Owner, false)]
    [InlineData(GroupRole.Member, GroupRole.Member, false)]
    public void CanRemove_FollowsTheHierarchy(GroupRole actor, GroupRole target, bool expected) =>
        Assert.Equal(expected, GroupRules.CanRemove(actor, target));

    [Fact]
    public void NextOwner_PrefersTheLongestStandingAdmin()
    {
        var t0 = DateTimeOffset.UtcNow;
        var oldMember = Member(GroupRole.Member, t0);
        var newAdmin = Member(GroupRole.Admin, t0.AddDays(5));
        var oldAdmin = Member(GroupRole.Admin, t0.AddDays(1));

        Assert.Same(oldAdmin, GroupRules.NextOwner([oldMember, newAdmin, oldAdmin]));
    }

    [Fact]
    public void NextOwner_WithoutAdmins_PicksTheLongestStandingMember()
    {
        var t0 = DateTimeOffset.UtcNow;
        var newer = Member(GroupRole.Member, t0.AddDays(2));
        var older = Member(GroupRole.Member, t0);

        Assert.Same(older, GroupRules.NextOwner([newer, older]));
    }

    private static GroupMember Member(GroupRole role, DateTimeOffset joinedAt) =>
        new() { GroupId = Guid.NewGuid(), UserId = Guid.NewGuid(), Role = role, Color = "#000000", JoinedAt = joinedAt };
}

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

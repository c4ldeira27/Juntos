using System.Net;
using Agenda.Api.Data;
using Agenda.Api.Groups;
using Agenda.Api.Schedule;
using Microsoft.EntityFrameworkCore;

namespace Agenda.Api.Tests.Integration;

public class GroupFlowTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string WeekQuery = "from=2026-11-09T00:00:00Z&to=2026-11-16T00:00:00Z";

    private static string TokenFrom(InviteDto invite) => invite.Url[(invite.Url.LastIndexOf('/') + 1)..];

    private static async Task<GroupDto> CreateGroupAsync(HttpClient owner, string name = "Us")
    {
        var created = await owner.PostJsonAsync("/api/groups", new CreateGroupRequest(name));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await created.ReadAsync<GroupDto>();
    }

    private static async Task<string> InviteAsync(HttpClient inviter, Guid groupId)
    {
        var response = await inviter.PostAsync($"/api/groups/{groupId}/invites", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return TokenFrom(await response.ReadAsync<InviteDto>());
    }

    private static async Task<(GroupDto Group, string Token)> CreateGroupWithInviteAsync(HttpClient owner, string name = "Us")
    {
        var group = await CreateGroupAsync(owner, name);
        return (group, await InviteAsync(owner, group.Id));
    }

    private static async Task JoinAsync(HttpClient owner, HttpClient newcomer, Guid groupId) =>
        Assert.Equal(HttpStatusCode.OK, (await newcomer.PostAsync($"/api/invites/{await InviteAsync(owner, groupId)}/accept", null)).StatusCode);

    /// <summary>Ana (owner) + Bruno (admin) + Carla (member).</summary>
    private async Task<(GroupDto Group, (Guid Id, HttpClient Client) Owner, (Guid Id, HttpClient Client) Admin, (Guid Id, HttpClient Client) Member)> CreateThreeRoleGroupAsync()
    {
        var owner = await api.CreateUserAsync("Ana");
        var admin = await api.CreateUserAsync("Bruno");
        var member = await api.CreateUserAsync("Carla");
        var group = await CreateGroupAsync(owner.Client, "Family");
        await JoinAsync(owner.Client, admin.Client, group.Id);
        await JoinAsync(owner.Client, member.Client, group.Id);

        var promoted = await owner.Client.PutJsonAsync($"/api/groups/{group.Id}/members/{admin.Id}/role", new ChangeRoleRequest(GroupRole.Admin));
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        return (await promoted.ReadAsync<GroupDto>(), owner, admin, member);
    }

    [Fact]
    public async Task FullFlow_CreateInviteAcceptShareAndLeave()
    {
        var (anaId, ana) = await api.CreateUserAsync("Ana");
        var (brunoId, bruno) = await api.CreateUserAsync("Bruno");

        // No groups yet: empty list (the front end shows the personal agenda)
        Assert.Empty(await (await ana.GetAsync("/api/groups")).ReadAsync<List<GroupSummaryDto>>());

        var (group, token) = await CreateGroupWithInviteAsync(ana);
        var owner = Assert.Single(group.Members);
        Assert.Equal(GroupRole.Owner, owner.Role);
        Assert.Equal(GroupRole.Owner, group.MyRole);
        Assert.Equal(GroupRules.MemberColors[0], owner.Color);
        Assert.False(owner.SharingConfigured);

        // Bruno previews the invite before accepting
        var preview = await (await bruno.GetAsync($"/api/invites/{token}")).ReadAsync<InvitePreviewDto>();
        Assert.Equal("valid", preview.Status);
        Assert.Equal("Ana", preview.InvitedBy);

        // Accepts: joins as a member with the second color
        var accepted = await bruno.PostAsync($"/api/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var joined = await accepted.ReadAsync<GroupDto>();
        Assert.Equal(2, joined.Members.Count);
        Assert.Equal(GroupRole.Member, joined.MyRole);
        var brunoMember = joined.Members.Single(m => m.UserId == brunoId);
        Assert.Equal(GroupRules.MemberColors[1], brunoMember.Color);
        Assert.True(brunoMember.IsMe);

        // Accepting again is idempotent (clicking the link twice isn't an error)
        Assert.Equal(HttpStatusCode.OK, (await bruno.PostAsync($"/api/invites/{token}/accept", null)).StatusCode);

        // A third person with the same link: already used
        var (_, carla) = await api.CreateUserAsync("Carla");
        var reused = await carla.PostAsync($"/api/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.Gone, reused.StatusCode);
        Assert.Equal("invite_invalid", await reused.ProblemCodeAsync());

        // Ana shares her primary calendar
        var sharing = await ana.PutJsonAsync($"/api/groups/{group.Id}/sharing",
            new UpdateSharingRequest([new SharingChoice("cal:primary", "Ana", SourceVisibility.Details)]));
        Assert.Equal(HttpStatusCode.NoContent, sharing.StatusCode);

        var current = await (await bruno.GetAsync($"/api/groups/{group.Id}")).ReadAsync<GroupDto>();
        var anaMember = current.Members.Single(m => m.UserId == anaId);
        Assert.True(anaMember.SharingConfigured);
        Assert.Equal(1, anaMember.SharedSources);

        // Ana (owner) leaves: Bruno becomes owner and her sharing is cascade-deleted
        Assert.Equal(HttpStatusCode.NoContent, (await ana.DeleteAsync($"/api/groups/{group.Id}/members/me")).StatusCode);
        await api.WithDbAsync(async db =>
        {
            Assert.Equal(GroupRole.Owner, (await db.GroupMembers.SingleAsync(m => m.GroupId == group.Id)).Role);
            Assert.False(await db.SharedSources.AnyAsync(s => s.UserId == anaId));
        });

        // The last member leaves: the group no longer exists
        Assert.Equal(HttpStatusCode.NoContent, (await bruno.DeleteAsync($"/api/groups/{group.Id}/members/me")).StatusCode);
        await api.WithDbAsync(async db => Assert.False(await db.Groups.AnyAsync(g => g.Id == group.Id)));
    }

    [Fact]
    public async Task MultipleGroups_EachWithItsOwnSharing()
    {
        var (anaId, ana) = await api.CreateUserAsync("Ana");
        var couple = await CreateGroupAsync(ana, "Couple");
        var family = await CreateGroupAsync(ana, "Family");

        var list = await (await ana.GetAsync("/api/groups")).ReadAsync<List<GroupSummaryDto>>();
        Assert.Equal(["Couple", "Family"], list.Select(g => g.Name));
        Assert.All(list, g => Assert.Equal(GroupRole.Owner, g.MyRole));

        // Details in one group, busy-only in the other: the choices don't leak between groups
        await ana.PutJsonAsync($"/api/groups/{couple.Id}/sharing",
            new UpdateSharingRequest([new SharingChoice("cal:primary", "Ana", SourceVisibility.Details)]));
        await ana.PutJsonAsync($"/api/groups/{family.Id}/sharing",
            new UpdateSharingRequest([new SharingChoice("cal:primary", "Ana", SourceVisibility.BusyOnly)]));

        await api.WithDbAsync(async db =>
        {
            var shares = await db.SharedSources.Where(s => s.UserId == anaId).ToListAsync();
            Assert.Equal(SourceVisibility.Details, shares.Single(s => s.GroupId == couple.Id).Visibility);
            Assert.Equal(SourceVisibility.BusyOnly, shares.Single(s => s.GroupId == family.Id).Visibility);
        });
    }

    [Fact]
    public async Task CreateGroup_BeyondTheLimit_IsRejected()
    {
        var (_, ana) = await api.CreateUserAsync("Ana");
        for (var i = 0; i < GroupRules.MaxGroupsPerUser; i++)
            await CreateGroupAsync(ana, $"Group {i}");

        var extra = await ana.PostJsonAsync("/api/groups", new CreateGroupRequest("One too many"));

        Assert.Equal(HttpStatusCode.Conflict, extra.StatusCode);
        Assert.Equal("too_many_groups", await extra.ProblemCodeAsync());
    }

    [Fact]
    public async Task Rename_AllowedForOwnerAndAdmin_NotForMember()
    {
        var (group, owner, admin, member) = await CreateThreeRoleGroupAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.Client.PatchAsync($"/api/groups/{group.Id}", JsonBody(new RenameGroupRequest("Nope")))).StatusCode);

        var renamed = await admin.Client.PatchAsync($"/api/groups/{group.Id}", JsonBody(new RenameGroupRequest("  Caldeimilo  ")));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Caldeimilo", (await renamed.ReadAsync<GroupDto>()).Name);

        Assert.Equal(HttpStatusCode.OK,
            (await owner.Client.PatchAsync($"/api/groups/{group.Id}", JsonBody(new RenameGroupRequest("Us")))).StatusCode);
    }

    [Fact]
    public async Task Invite_AllowedForOwnerAndAdmin_NotForMember()
    {
        var (group, _, admin, member) = await CreateThreeRoleGroupAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/groups/{group.Id}/invites", null)).StatusCode);

        var denied = await member.Client.PostAsync($"/api/groups/{group.Id}/invites", null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("forbidden", await denied.ProblemCodeAsync());
    }

    [Fact]
    public async Task ChangeRole_OnlyOwner_AndNeverOnTheOwner()
    {
        var (group, owner, admin, member) = await CreateThreeRoleGroupAsync();

        // Admins can't promote
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.Client.PutJsonAsync($"/api/groups/{group.Id}/members/{member.Id}/role", new ChangeRoleRequest(GroupRole.Admin))).StatusCode);
        // Ownership isn't handed out through this endpoint
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.Client.PutJsonAsync($"/api/groups/{group.Id}/members/{member.Id}/role", new ChangeRoleRequest(GroupRole.Owner))).StatusCode);
        // The owner can't be demoted
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.Client.PutJsonAsync($"/api/groups/{group.Id}/members/{owner.Id}/role", new ChangeRoleRequest(GroupRole.Member))).StatusCode);

        // The owner demotes the admin
        var demoted = await owner.Client.PutJsonAsync($"/api/groups/{group.Id}/members/{admin.Id}/role", new ChangeRoleRequest(GroupRole.Member));
        Assert.Equal(HttpStatusCode.OK, demoted.StatusCode);
        Assert.Equal(GroupRole.Member, (await demoted.ReadAsync<GroupDto>()).Members.Single(m => m.UserId == admin.Id).Role);
    }

    [Fact]
    public async Task RemoveMember_FollowsTheHierarchy()
    {
        var (group, owner, admin, member) = await CreateThreeRoleGroupAsync();

        // Members remove nobody; admins can't remove the owner; nobody removes themselves through this route
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.DeleteAsync($"/api/groups/{group.Id}/members/{admin.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.DeleteAsync($"/api/groups/{group.Id}/members/{owner.Id}")).StatusCode);
        var self = await admin.Client.DeleteAsync($"/api/groups/{group.Id}/members/{admin.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal("use_leave", await self.ProblemCodeAsync());

        // The member shared a calendar; the admin removes them and the sharing goes too
        await member.Client.PutJsonAsync($"/api/groups/{group.Id}/sharing",
            new UpdateSharingRequest([new SharingChoice("cal:primary", "Carla", SourceVisibility.Details)]));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.DeleteAsync($"/api/groups/{group.Id}/members/{member.Id}")).StatusCode);
        await api.WithDbAsync(async db =>
        {
            Assert.False(await db.GroupMembers.AnyAsync(m => m.GroupId == group.Id && m.UserId == member.Id));
            Assert.False(await db.SharedSources.AnyAsync(s => s.GroupId == group.Id && s.UserId == member.Id));
        });
        // The removed person no longer sees the group
        Assert.Equal(HttpStatusCode.NotFound, (await member.Client.GetAsync($"/api/groups/{group.Id}")).StatusCode);

        // The owner can remove an admin
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"/api/groups/{group.Id}/members/{admin.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteGroup_OnlyOwner()
    {
        var (group, owner, admin, _) = await CreateThreeRoleGroupAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.DeleteAsync($"/api/groups/{group.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"/api/groups/{group.Id}")).StatusCode);

        await api.WithDbAsync(async db =>
        {
            Assert.False(await db.Groups.AnyAsync(g => g.Id == group.Id));
            Assert.False(await db.GroupMembers.AnyAsync(m => m.GroupId == group.Id));
        });
    }

    [Fact]
    public async Task Leave_OwnerLeaving_PromotesTheAdminBeforeOlderMembers()
    {
        var (group, owner, admin, member) = await CreateThreeRoleGroupAsync();
        // Carla (member) joined after Bruno, but even an older plain member would lose to the admin

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"/api/groups/{group.Id}/members/me")).StatusCode);

        var after = await (await member.Client.GetAsync($"/api/groups/{group.Id}")).ReadAsync<GroupDto>();
        Assert.Equal(GroupRole.Owner, after.Members.Single(m => m.UserId == admin.Id).Role);
    }

    [Fact]
    public async Task AcceptInvite_Expired_IsRejected()
    {
        var (_, ana) = await api.CreateUserAsync("Ana");
        var (_, bruno) = await api.CreateUserAsync("Bruno");
        var (group, token) = await CreateGroupWithInviteAsync(ana);

        await api.WithDbAsync(db => db.GroupInvites
            .Where(i => i.GroupId == group.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));

        var preview = await (await bruno.GetAsync($"/api/invites/{token}")).ReadAsync<InvitePreviewDto>();
        Assert.Equal("expired", preview.Status);
        Assert.Equal(HttpStatusCode.Gone, (await bruno.PostAsync($"/api/invites/{token}/accept", null)).StatusCode);
    }

    [Fact]
    public async Task CreateInvite_ReturnsEnglishInviteRoute()
    {
        var (_, ana) = await api.CreateUserAsync("Ana");
        var group = await CreateGroupAsync(ana);

        var invite = await (await ana.PostAsync($"/api/groups/{group.Id}/invites", null)).ReadAsync<InviteDto>();

        Assert.Matches("/invite/[A-Za-z0-9_-]{43}$", invite.Url);
    }

    [Fact]
    public async Task PreviewInvite_UnknownToken_Returns404()
    {
        var (_, ana) = await api.CreateUserAsync("Ana");

        var response = await ana.GetAsync("/api/invites/token-that-does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("invite_not_found", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task GroupEndpoints_NonMember_Gets404Everywhere()
    {
        var (_, ana) = await api.CreateUserAsync("Ana");
        var (_, intruder) = await api.CreateUserAsync("Intruder");
        var group = await CreateGroupAsync(ana);

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/groups/{group.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/groups/{group.Id}/agenda?{WeekQuery}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsync($"/api/groups/{group.Id}/invites", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.DeleteAsync($"/api/groups/{group.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruder.PatchAsync($"/api/groups/{group.Id}", JsonBody(new RenameGroupRequest("x")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruder.PutJsonAsync($"/api/groups/{group.Id}/sharing", new UpdateSharingRequest([]))).StatusCode);
    }

    [Fact]
    public async Task GroupAgenda_MemberWithoutGoogleConnection_IsUnavailableWithoutFailingTheResponse()
    {
        var (anaId, ana) = await api.CreateUserAsync("Ana");
        var group = await CreateGroupAsync(ana);
        await ana.PutJsonAsync($"/api/groups/{group.Id}/sharing",
            new UpdateSharingRequest([new SharingChoice("cal:primary", "Ana", SourceVisibility.Details)]));

        // Ana has no GoogleConnection in the database (test user): reading her calendar fails, the response doesn't.
        var response = await ana.GetAsync($"/api/groups/{group.Id}/agenda?{WeekQuery}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var agenda = await response.ReadAsync<GroupAgendaResponse>();
        Assert.Empty(agenda.Items);
        Assert.Contains(anaId, agenda.UnavailableMembers);
    }

    [Fact]
    public async Task Requests_InvalidInput_Return400()
    {
        var (_, ana) = await api.CreateUserAsync("Ana");

        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostJsonAsync("/api/groups", new CreateGroupRequest("  "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ana.PostJsonAsync("/api/groups", new CreateGroupRequest(new string('x', 61)))).StatusCode);

        var group = await CreateGroupAsync(ana);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ana.PatchAsync($"/api/groups/{group.Id}", JsonBody(new RenameGroupRequest("")))).StatusCode);
        var invalidSource = await ana.PutJsonAsync($"/api/groups/{group.Id}/sharing",
            new UpdateSharingRequest([new SharingChoice("outlook:abc", "x", SourceVisibility.Details)]));
        Assert.Equal(HttpStatusCode.BadRequest, invalidSource.StatusCode);
    }

    [Fact]
    public async Task Requests_WithoutLogin_Return401()
    {
        var anonymous = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/groups")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/invites/x/accept", null)).StatusCode);
    }

    private static HttpContent JsonBody<T>(T body) => System.Net.Http.Json.JsonContent.Create(body, options: ApiFactory.Json);
}

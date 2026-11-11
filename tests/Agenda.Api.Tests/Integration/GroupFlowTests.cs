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

    private static async Task<(GroupDto Group, string Token)> CreateGroupWithInviteAsync(HttpClient owner, string name = "Us")
    {
        var created = await owner.PostJsonAsync("/api/groups", new CreateGroupRequest(name));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var group = await created.ReadAsync<GroupDto>();

        var inviteResponse = await owner.PostAsync($"/api/groups/{group.Id}/invites", null);
        Assert.Equal(HttpStatusCode.OK, inviteResponse.StatusCode);
        return (group, TokenFrom(await inviteResponse.ReadAsync<InviteDto>()));
    }

    [Fact]
    public async Task FullFlow_CreateInviteAcceptShareAndLeave()
    {
        var (anaId, ana) = await api.CreateUserAsync("Ana");
        var (brunoId, bruno) = await api.CreateUserAsync("Bruno");

        // No group yet: 204
        Assert.Equal(HttpStatusCode.NoContent, (await ana.GetAsync("/api/groups/current")).StatusCode);

        var (group, token) = await CreateGroupWithInviteAsync(ana);
        var owner = Assert.Single(group.Members);
        Assert.Equal(GroupRole.Owner, owner.Role);
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
        var brunoMember = joined.Members.Single(m => m.UserId == brunoId);
        Assert.Equal(GroupRole.Member, brunoMember.Role);
        Assert.Equal(GroupRules.MemberColors[1], brunoMember.Color);
        Assert.True(brunoMember.IsMe);

        // Accepting again is idempotent (clicking the link twice isn't an error)
        Assert.Equal(HttpStatusCode.OK, (await bruno.PostAsync($"/api/invites/{token}/accept", null)).StatusCode);

        // A third person with the same link: already used
        var (_, carla) = await api.CreateUserAsync("Carla");
        var reused = await carla.PostAsync($"/api/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.Gone, reused.StatusCode);
        Assert.Equal("invite_invalid", await reused.ProblemCodeAsync());

        // MVP rule: one group per person
        var second = await ana.PostJsonAsync("/api/groups", new CreateGroupRequest("Another"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_in_group", await second.ProblemCodeAsync());

        // Ana shares her primary calendar
        var sharing = await ana.PutJsonAsync($"/api/groups/{group.Id}/sharing",
            new UpdateSharingRequest([new SharingChoice("cal:primary", "Ana", SourceVisibility.Details)]));
        Assert.Equal(HttpStatusCode.NoContent, sharing.StatusCode);

        var current = await (await bruno.GetAsync("/api/groups/current")).ReadAsync<GroupDto>();
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
    public async Task CreateInvite_ReturnsEnglishInviteRoute()
    {
        var (_, ana) = await api.CreateUserAsync("Ana");
        var created = await (await ana.PostJsonAsync("/api/groups", new CreateGroupRequest("Us"))).ReadAsync<GroupDto>();

        var invite = await (await ana.PostAsync($"/api/groups/{created.Id}/invites", null)).ReadAsync<InviteDto>();

        Assert.Matches("/invite/[A-Za-z0-9_-]{43}$", invite.Url);
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
        var (group, _) = await CreateGroupWithInviteAsync(ana);

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/groups/{group.Id}/agenda?{WeekQuery}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsync($"/api/groups/{group.Id}/invites", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruder.PutJsonAsync($"/api/groups/{group.Id}/sharing", new UpdateSharingRequest([]))).StatusCode);
    }

    [Fact]
    public async Task GroupAgenda_MemberWithoutGoogleConnection_IsUnavailableWithoutFailingTheResponse()
    {
        var (anaId, ana) = await api.CreateUserAsync("Ana");
        var (group, _) = await CreateGroupWithInviteAsync(ana);
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

        var (group, _) = await CreateGroupWithInviteAsync(ana);
        var invalidSource = await ana.PutJsonAsync($"/api/groups/{group.Id}/sharing",
            new UpdateSharingRequest([new SharingChoice("outlook:abc", "x", SourceVisibility.Details)]));
        Assert.Equal(HttpStatusCode.BadRequest, invalidSource.StatusCode);
    }

    [Fact]
    public async Task Requests_WithoutLogin_Return401()
    {
        var anonymous = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/groups/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/invites/x/accept", null)).StatusCode);
    }
}

using System.Net;
using Agenda.Api.Data;
using Agenda.Api.Groups;
using Agenda.Api.Schedule;
using Microsoft.EntityFrameworkCore;

namespace Agenda.Api.Tests.Integration;

public class GroupFlowTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static string TokenFrom(InviteDto invite) => invite.Url[(invite.Url.LastIndexOf('/') + 1)..];

    private static async Task<(GroupDto Group, string Token)> CreateGroupWithInviteAsync(HttpClient owner, string name = "Nós")
    {
        var created = await owner.PostJsonAsync("/api/groups", new CreateGroupRequest(name));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var group = await created.ReadAsync<GroupDto>();

        var inviteResponse = await owner.PostAsync($"/api/groups/{group.Id}/invites", null);
        Assert.Equal(HttpStatusCode.OK, inviteResponse.StatusCode);
        return (group, TokenFrom(await inviteResponse.ReadAsync<InviteDto>()));
    }

    [Fact]
    public async Task Fluxo_completo_criar_convidar_aceitar_compartilhar_e_sair()
    {
        var (anaId, ana) = await api.CreateUserAsync("Ana");
        var (brunoId, bruno) = await api.CreateUserAsync("Bruno");

        // Sem grupo ainda: 204
        Assert.Equal(HttpStatusCode.NoContent, (await ana.GetAsync("/api/groups/current")).StatusCode);

        var (group, token) = await CreateGroupWithInviteAsync(ana);
        var owner = Assert.Single(group.Members);
        Assert.Equal(GroupRole.Owner, owner.Role);
        Assert.Equal(GroupRules.MemberColors[0], owner.Color);
        Assert.False(owner.SharingConfigured);

        // Bruno vê o convite antes de aceitar
        var preview = await (await bruno.GetAsync($"/api/invites/{token}")).ReadAsync<InvitePreviewDto>();
        Assert.Equal("valid", preview.Status);
        Assert.Equal("Ana", preview.InvitedBy);

        // Aceita: entra como membro com a segunda cor
        var accepted = await bruno.PostAsync($"/api/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var joined = await accepted.ReadAsync<GroupDto>();
        Assert.Equal(2, joined.Members.Count);
        var brunoMember = joined.Members.Single(m => m.UserId == brunoId);
        Assert.Equal(GroupRole.Member, brunoMember.Role);
        Assert.Equal(GroupRules.MemberColors[1], brunoMember.Color);
        Assert.True(brunoMember.IsMe);

        // Aceitar de novo é idempotente (clicar duas vezes no link não é erro)
        Assert.Equal(HttpStatusCode.OK, (await bruno.PostAsync($"/api/invites/{token}/accept", null)).StatusCode);

        // Uma terceira pessoa com o mesmo link: já foi usado
        var (_, carla) = await api.CreateUserAsync("Carla");
        var reused = await carla.PostAsync($"/api/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.Gone, reused.StatusCode);
        Assert.Equal("invite_invalid", await reused.ProblemCodeAsync());

        // Regra do MVP: um grupo por pessoa
        var second = await ana.PostJsonAsync("/api/groups", new CreateGroupRequest("Outro"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_in_group", await second.ProblemCodeAsync());

        // Ana compartilha a agenda principal
        var sharing = await ana.PutJsonAsync($"/api/groups/{group.Id}/sharing",
            new UpdateSharingRequest([new SharingChoice("cal:primary", "Ana", SourceVisibility.Details)]));
        Assert.Equal(HttpStatusCode.NoContent, sharing.StatusCode);

        var current = await (await bruno.GetAsync("/api/groups/current")).ReadAsync<GroupDto>();
        var anaMember = current.Members.Single(m => m.UserId == anaId);
        Assert.True(anaMember.SharingConfigured);
        Assert.Equal(1, anaMember.SharedSources);

        // Ana (dona) sai: Bruno vira dono e o compartilhamento dela some em cascata
        Assert.Equal(HttpStatusCode.NoContent, (await ana.DeleteAsync($"/api/groups/{group.Id}/members/me")).StatusCode);
        await api.WithDbAsync(async db =>
        {
            Assert.Equal(GroupRole.Owner, (await db.GroupMembers.SingleAsync(m => m.GroupId == group.Id)).Role);
            Assert.False(await db.SharedSources.AnyAsync(s => s.UserId == anaId));
        });

        // Último membro sai: o grupo deixa de existir
        Assert.Equal(HttpStatusCode.NoContent, (await bruno.DeleteAsync($"/api/groups/{group.Id}/members/me")).StatusCode);
        await api.WithDbAsync(async db => Assert.False(await db.Groups.AnyAsync(g => g.Id == group.Id)));
    }

    [Fact]
    public async Task Convite_expirado_e_recusado()
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
    public async Task Token_inexistente_da_404()
    {
        var (_, ana) = await api.CreateUserAsync("Ana");

        var response = await ana.GetAsync("/api/invites/token-que-nao-existe");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("invite_not_found", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Quem_nao_e_membro_recebe_404_em_tudo_do_grupo()
    {
        var (_, ana) = await api.CreateUserAsync("Ana");
        var (_, intruso) = await api.CreateUserAsync("Intruso");
        var (group, _) = await CreateGroupWithInviteAsync(ana);

        Assert.Equal(HttpStatusCode.NotFound,
            (await intruso.GetAsync($"/api/groups/{group.Id}/agenda?from=2026-11-09T00:00:00Z&to=2026-11-16T00:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruso.PostAsync($"/api/groups/{group.Id}/invites", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruso.PutJsonAsync($"/api/groups/{group.Id}/sharing", new UpdateSharingRequest([]))).StatusCode);
    }

    [Fact]
    public async Task Agenda_do_grupo_marca_membro_sem_conexao_google_como_indisponivel_sem_derrubar_a_resposta()
    {
        var (anaId, ana) = await api.CreateUserAsync("Ana");
        var (group, _) = await CreateGroupWithInviteAsync(ana);
        await ana.PutJsonAsync($"/api/groups/{group.Id}/sharing",
            new UpdateSharingRequest([new SharingChoice("cal:primary", "Ana", SourceVisibility.Details)]));

        // Ana não tem GoogleConnection no banco (usuário de teste): a leitura dela falha, a resposta não.
        var response = await ana.GetAsync($"/api/groups/{group.Id}/agenda?from=2026-11-09T00:00:00Z&to=2026-11-16T00:00:00Z");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var agenda = await response.ReadAsync<GroupAgendaResponse>();
        Assert.Empty(agenda.Items);
        Assert.Contains(anaId, agenda.UnavailableMembers);
    }

    [Fact]
    public async Task Validacoes_de_entrada()
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
    public async Task Sem_login_recebe_401()
    {
        var anonymous = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/groups/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/invites/x/accept", null)).StatusCode);
    }
}

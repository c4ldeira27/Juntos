using Agenda.Api.Schedule;
using Google.Apis.Calendar.v3.Data;
using GoogleTask = Google.Apis.Tasks.v1.Data.Task;

namespace Agenda.Api.Tests;

public class AgendaMapperTests
{
    private static readonly SourceId Source = SourceId.Calendar("primary");

    private static Event TimedEvent(Action<Event>? configure = null)
    {
        var ev = new Event
        {
            Id = "ev1",
            Summary = "Dentista",
            Location = "Savassi",
            Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(2026, 11, 10, 14, 0, 0, TimeSpan.FromHours(-3)) },
            End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.FromHours(-3)) },
        };
        configure?.Invoke(ev);
        return ev;
    }

    [Fact]
    public void Evento_com_hora_e_normalizado_para_utc()
    {
        var item = AgendaMapper.FromEvent(TimedEvent(), Source, SourceVisibility.Details, viewerIsOwner: false);

        Assert.NotNull(item);
        Assert.Equal("2026-11-10T17:00:00Z", item.Start); // 14h em Brasília = 17h UTC
        Assert.Equal("2026-11-10T18:00:00Z", item.End);
        Assert.False(item.AllDay);
        Assert.Equal(AgendaItemKind.Event, item.Kind);
        Assert.Equal("Dentista", item.Title);
        Assert.Equal("cal:primary", item.SourceId);
    }

    [Fact]
    public void Evento_de_dia_inteiro_mantem_datas_e_fim_exclusivo()
    {
        var ev = TimedEvent(e =>
        {
            e.Start = new EventDateTime { Date = "2026-11-15" };
            e.End = new EventDateTime { Date = "2026-11-16" };
        });

        var item = AgendaMapper.FromEvent(ev, Source, SourceVisibility.Details, viewerIsOwner: false);

        Assert.NotNull(item);
        Assert.True(item.AllDay);
        Assert.Equal("2026-11-15", item.Start);
        Assert.Equal("2026-11-16", item.End);
    }

    [Theory]
    [InlineData("cancelled", null)]
    [InlineData(null, "workingLocation")]
    public void Eventos_cancelados_ou_de_local_de_trabalho_somem(string? status, string? eventType)
    {
        var ev = TimedEvent(e => { e.Status = status; e.EventType = eventType; });

        Assert.Null(AgendaMapper.FromEvent(ev, Source, SourceVisibility.Details, viewerIsOwner: true));
    }

    [Fact]
    public void Convite_recusado_some()
    {
        var ev = TimedEvent(e => e.Attendees =
        [
            new EventAttendee { Self = true, ResponseStatus = "declined" },
            new EventAttendee { Self = false, ResponseStatus = "accepted" },
        ]);

        Assert.Null(AgendaMapper.FromEvent(ev, Source, SourceVisibility.Details, viewerIsOwner: true));
    }

    [Theory]
    [InlineData("private")]
    [InlineData("confidential")]
    public void Evento_privado_vira_ocupado_para_outros_membros(string visibility)
    {
        var ev = TimedEvent(e => e.Visibility = visibility);

        var item = AgendaMapper.FromEvent(ev, Source, SourceVisibility.Details, viewerIsOwner: false);

        Assert.NotNull(item);
        Assert.Equal(AgendaItemKind.Busy, item.Kind);
        Assert.Null(item.Title);
        Assert.Null(item.Location);
    }

    [Fact]
    public void Dono_ve_os_detalhes_do_proprio_evento_privado()
    {
        var ev = TimedEvent(e => e.Visibility = "private");

        var item = AgendaMapper.FromEvent(ev, Source, SourceVisibility.BusyOnly, viewerIsOwner: true);

        Assert.NotNull(item);
        Assert.Equal(AgendaItemKind.Event, item.Kind);
        Assert.Equal("Dentista", item.Title);
    }

    [Fact]
    public void Agenda_compartilhada_so_como_ocupado_esconde_detalhes()
    {
        var item = AgendaMapper.FromEvent(TimedEvent(), Source, SourceVisibility.BusyOnly, viewerIsOwner: false);

        Assert.NotNull(item);
        Assert.Equal(AgendaItemKind.Busy, item.Kind);
        Assert.Null(item.Title);
    }

    [Fact]
    public void Evento_disponivel_some_quando_so_mostraria_ocupado()
    {
        var ev = TimedEvent(e => e.Transparency = "transparent");

        Assert.Null(AgendaMapper.FromEvent(ev, Source, SourceVisibility.BusyOnly, viewerIsOwner: false));
        Assert.NotNull(AgendaMapper.FromEvent(ev, Source, SourceVisibility.Details, viewerIsOwner: false));
    }

    [Fact]
    public void Evento_sem_titulo_recebe_texto_padrao()
    {
        var item = AgendaMapper.FromEvent(TimedEvent(e => e.Summary = "  "), Source, SourceVisibility.Details, true);

        Assert.Equal("(sem título)", item?.Title);
    }

    [Fact]
    public void Tarefa_vira_item_de_dia_inteiro_sem_deslocar_o_dia_pelo_fuso()
    {
        var task = new GoogleTask { Id = "t1", Title = "Pagar aluguel", Due = "2026-11-12T00:00:00.000Z", Status = "needsAction" };

        var item = AgendaMapper.FromTask(task, SourceId.TaskList("lista"));

        Assert.NotNull(item);
        Assert.Equal(AgendaItemKind.Task, item.Kind);
        Assert.True(item.AllDay);
        Assert.Equal("2026-11-12", item.Start);
        Assert.Equal("2026-11-13", item.End);
        Assert.Equal("tasks:lista", item.SourceId);
    }

    [Theory]
    [InlineData("completed", null, null, "2026-11-12T00:00:00.000Z")]
    [InlineData("needsAction", true, null, "2026-11-12T00:00:00.000Z")]
    [InlineData("needsAction", null, true, "2026-11-12T00:00:00.000Z")]
    [InlineData("needsAction", null, null, null)]
    public void Tarefas_concluidas_apagadas_ocultas_ou_sem_data_somem(string status, bool? deleted, bool? hidden, string? due)
    {
        var task = new GoogleTask { Id = "t1", Title = "x", Status = status, Deleted = deleted, Hidden = hidden, Due = due };

        Assert.Null(AgendaMapper.FromTask(task, SourceId.TaskList("lista")));
    }
}

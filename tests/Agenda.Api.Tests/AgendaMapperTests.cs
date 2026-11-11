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
            Summary = "Dentist",
            Location = "Savassi",
            Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(2026, 11, 10, 14, 0, 0, TimeSpan.FromHours(-3)) },
            End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.FromHours(-3)) },
        };
        configure?.Invoke(ev);
        return ev;
    }

    [Fact]
    public void FromEvent_TimedEvent_IsNormalizedToUtc()
    {
        var item = AgendaMapper.FromEvent(TimedEvent(), Source, SourceVisibility.Details, viewerIsOwner: false);

        Assert.NotNull(item);
        Assert.Equal("2026-11-10T17:00:00Z", item.Start); // 2 PM in Brasília = 5 PM UTC
        Assert.Equal("2026-11-10T18:00:00Z", item.End);
        Assert.False(item.AllDay);
        Assert.Equal(AgendaItemKind.Event, item.Kind);
        Assert.Equal("Dentist", item.Title);
        Assert.Equal("cal:primary", item.SourceId);
    }

    [Fact]
    public void FromEvent_AllDayEvent_KeepsDatesWithExclusiveEnd()
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
    public void FromEvent_CancelledOrWorkingLocation_IsHidden(string? status, string? eventType)
    {
        var ev = TimedEvent(e => { e.Status = status; e.EventType = eventType; });

        Assert.Null(AgendaMapper.FromEvent(ev, Source, SourceVisibility.Details, viewerIsOwner: true));
    }

    [Fact]
    public void FromEvent_DeclinedInvitation_IsHidden()
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
    public void FromEvent_PrivateEvent_BecomesBusyForOtherMembers(string visibility)
    {
        var ev = TimedEvent(e => e.Visibility = visibility);

        var item = AgendaMapper.FromEvent(ev, Source, SourceVisibility.Details, viewerIsOwner: false);

        Assert.NotNull(item);
        Assert.Equal(AgendaItemKind.Busy, item.Kind);
        Assert.Null(item.Title);
        Assert.Null(item.Location);
    }

    [Fact]
    public void FromEvent_Owner_SeesDetailsOfOwnPrivateEvent()
    {
        var ev = TimedEvent(e => e.Visibility = "private");

        var item = AgendaMapper.FromEvent(ev, Source, SourceVisibility.BusyOnly, viewerIsOwner: true);

        Assert.NotNull(item);
        Assert.Equal(AgendaItemKind.Event, item.Kind);
        Assert.Equal("Dentist", item.Title);
    }

    [Fact]
    public void FromEvent_BusyOnlySource_HidesDetails()
    {
        var item = AgendaMapper.FromEvent(TimedEvent(), Source, SourceVisibility.BusyOnly, viewerIsOwner: false);

        Assert.NotNull(item);
        Assert.Equal(AgendaItemKind.Busy, item.Kind);
        Assert.Null(item.Title);
    }

    [Fact]
    public void FromEvent_FreeEvent_IsHiddenWhenItWouldOnlyShowAsBusy()
    {
        var ev = TimedEvent(e => e.Transparency = "transparent");

        Assert.Null(AgendaMapper.FromEvent(ev, Source, SourceVisibility.BusyOnly, viewerIsOwner: false));
        Assert.NotNull(AgendaMapper.FromEvent(ev, Source, SourceVisibility.Details, viewerIsOwner: false));
    }

    [Fact]
    public void FromEvent_WithoutTitle_ReturnsNullTitleForTheFrontEndToLocalize()
    {
        var item = AgendaMapper.FromEvent(TimedEvent(e => e.Summary = "  "), Source, SourceVisibility.Details, true);

        Assert.NotNull(item);
        Assert.Equal(AgendaItemKind.Event, item.Kind);
        Assert.Null(item.Title);
    }

    [Fact]
    public void FromTask_BecomesAllDayItemWithoutShiftingTheDayByTimeZone()
    {
        var task = new GoogleTask { Id = "t1", Title = "Pay rent", Due = "2026-11-12T00:00:00.000Z", Status = "needsAction" };

        var item = AgendaMapper.FromTask(task, SourceId.TaskList("list"));

        Assert.NotNull(item);
        Assert.Equal(AgendaItemKind.Task, item.Kind);
        Assert.True(item.AllDay);
        Assert.Equal("2026-11-12", item.Start);
        Assert.Equal("2026-11-13", item.End);
        Assert.Equal("tasks:list", item.SourceId);
    }

    [Theory]
    [InlineData("completed", null, null, "2026-11-12T00:00:00.000Z")]
    [InlineData("needsAction", true, null, "2026-11-12T00:00:00.000Z")]
    [InlineData("needsAction", null, true, "2026-11-12T00:00:00.000Z")]
    [InlineData("needsAction", null, null, null)]
    public void FromTask_CompletedDeletedHiddenOrUndated_IsHidden(string status, bool? deleted, bool? hidden, string? due)
    {
        var task = new GoogleTask { Id = "t1", Title = "x", Status = status, Deleted = deleted, Hidden = hidden, Due = due };

        Assert.Null(AgendaMapper.FromTask(task, SourceId.TaskList("list")));
    }
}

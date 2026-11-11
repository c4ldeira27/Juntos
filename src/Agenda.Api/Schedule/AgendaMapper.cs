using System.Globalization;
using Google.Apis.Calendar.v3.Data;
using GoogleTask = Google.Apis.Tasks.v1.Data.Task;

namespace Agenda.Api.Schedule;

/// <summary>
/// Business rules for "what shows up and how". Pure functions (no I/O),
/// which makes them the easiest and most important place to test.
/// </summary>
public static class AgendaMapper
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <param name="viewerIsOwner">Owners always see everything in their own calendar; restrictions apply to other members.</param>
    public static AgendaItem? FromEvent(Event ev, SourceId source, SourceVisibility visibility, bool viewerIsOwner)
    {
        if (ev.Status == "cancelled") return null;

        // "Working from home/office" becomes an event in Google, but it isn't a commitment.
        if (ev.EventType == "workingLocation") return null;

        // A declined invitation doesn't keep anyone busy.
        if (ev.Attendees?.Any(a => a.Self == true && a.ResponseStatus == "declined") == true) return null;

        var isPrivate = ev.Visibility is "private" or "confidential";
        var showDetails = viewerIsOwner || (visibility == SourceVisibility.Details && !isPrivate);

        // Event marked as "Free" in Google: if we can't show the details,
        // a "Busy" block would lie about availability. So it disappears.
        if (!showDetails && ev.Transparency == "transparent") return null;

        if (!TryGetRange(ev, out var start, out var end, out var allDay)) return null;

        return new AgendaItem(
            Id: $"{source}/{ev.Id}",
            SourceId: source.ToString(),
            Kind: showDetails ? AgendaItemKind.Event : AgendaItemKind.Busy,
            Title: showDetails ? NullIfBlank(ev.Summary) : null,
            Start: start,
            End: end,
            AllDay: allDay,
            Location: showDetails ? ev.Location : null);
    }

    /// <summary>
    /// The Google Tasks API stores only the due DATE (the time is discarded),
    /// so every task becomes an all-day item.
    /// </summary>
    public static AgendaItem? FromTask(GoogleTask task, SourceId source)
    {
        if (task.Deleted == true || task.Hidden == true || task.Status == "completed") return null;

        // "due" comes as "2026-11-12T00:00:00.000Z". We take only the date, without converting time zones:
        // converting to Brasília time would move the task to the previous day (9 PM on the 11th).
        if (task.Due is not { Length: >= 10 } due) return null;
        if (!DateOnly.TryParseExact(due[..10], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;

        return new AgendaItem(
            Id: $"{source}/{task.Id}",
            SourceId: source.ToString(),
            Kind: AgendaItemKind.Task,
            Title: NullIfBlank(task.Title),
            Start: date.ToString(DateFormat, CultureInfo.InvariantCulture),
            End: date.AddDays(1).ToString(DateFormat, CultureInfo.InvariantCulture),
            AllDay: true,
            Location: null);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool TryGetRange(Event ev, out string start, out string end, out bool allDay)
    {
        // All-day: Google fills "date" (not "dateTime"). The end is exclusive.
        if (ev.Start?.Date is { Length: > 0 } startDate)
        {
            start = startDate;
            end = ev.End?.Date is { Length: > 0 } endDate
                ? endDate
                : DateOnly.ParseExact(startDate, DateFormat, CultureInfo.InvariantCulture).AddDays(1)
                    .ToString(DateFormat, CultureInfo.InvariantCulture);
            allDay = true;
            return true;
        }

        // Timed: normalized to UTC. Converting to the viewer's time zone is the front end's job.
        if (ev.Start?.DateTimeDateTimeOffset is { } s && ev.End?.DateTimeDateTimeOffset is { } e)
        {
            start = s.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture);
            end = e.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture);
            allDay = false;
            return true;
        }

        start = end = "";
        allDay = false;
        return false;
    }
}

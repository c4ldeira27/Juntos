namespace Agenda.Api.GoogleApi;

public static class GoogleScopes
{
    /// <summary>Required: without it the app has nothing to show.</summary>
    public const string CalendarReadonly = "https://www.googleapis.com/auth/calendar.readonly";

    /// <summary>Optional: if the user unchecks it, the app works without tasks.</summary>
    public const string TasksReadonly = "https://www.googleapis.com/auth/tasks.readonly";

    public static readonly string[] Requested = [CalendarReadonly, TasksReadonly];
}

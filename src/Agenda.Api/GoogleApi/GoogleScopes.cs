namespace Agenda.Api.GoogleApi;

public static class GoogleScopes
{
    /// <summary>Obrigatório: sem ele o app não tem o que mostrar.</summary>
    public const string CalendarReadonly = "https://www.googleapis.com/auth/calendar.readonly";

    /// <summary>Opcional: se o usuário desmarcar, o app funciona sem tarefas.</summary>
    public const string TasksReadonly = "https://www.googleapis.com/auth/tasks.readonly";

    public static readonly string[] Requested = [CalendarReadonly, TasksReadonly];
}

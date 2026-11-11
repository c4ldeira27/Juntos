using Agenda.Api.Auth;
using Agenda.Api.Schedule;

namespace Agenda.Api.Tests;

public class SourceIdTests
{
    [Theory]
    [InlineData("cal:primary", SourceKind.Calendar, "primary")]
    [InlineData("cal:pt.brazilian#holiday@group.v.calendar.google.com", SourceKind.Calendar, "pt.brazilian#holiday@group.v.calendar.google.com")]
    [InlineData("tasks:MTIzNDU2", SourceKind.TaskList, "MTIzNDU2")]
    public void Parse_e_ToString_fazem_ida_e_volta(string raw, SourceKind kind, string googleId)
    {
        Assert.True(SourceId.TryParse(raw, out var id));
        Assert.Equal(kind, id.Kind);
        Assert.Equal(googleId, id.GoogleId);
        Assert.Equal(raw, id.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cal:")]
    [InlineData("primary")]
    [InlineData("outlook:abc")]
    public void Valores_invalidos_sao_rejeitados(string? raw) =>
        Assert.False(SourceId.TryParse(raw, out _));
}

public class SafeLocalUrlTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/agenda?semana=2", "/agenda?semana=2")]
    [InlineData(null, "/")]
    [InlineData("https://site-falso.com", "/")]
    [InlineData("//site-falso.com", "/")]
    [InlineData("/\\site-falso.com", "/")]
    public void So_aceita_urls_locais(string? input, string expected) =>
        Assert.Equal(expected, AuthEndpoints.SafeLocalUrl(input));
}

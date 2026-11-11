using Agenda.Api.Auth;
using Agenda.Api.Schedule;

namespace Agenda.Api.Tests;

public class SourceIdTests
{
    [Theory]
    [InlineData("cal:primary", SourceKind.Calendar, "primary")]
    [InlineData("cal:pt.brazilian#holiday@group.v.calendar.google.com", SourceKind.Calendar, "pt.brazilian#holiday@group.v.calendar.google.com")]
    [InlineData("tasks:MTIzNDU2", SourceKind.TaskList, "MTIzNDU2")]
    public void TryParse_AndToString_RoundTrip(string raw, SourceKind kind, string googleId)
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
    public void TryParse_InvalidValue_IsRejected(string? raw) =>
        Assert.False(SourceId.TryParse(raw, out _));
}

public class SafeLocalUrlTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/agenda?week=2", "/agenda?week=2")]
    [InlineData(null, "/")]
    [InlineData("https://fake-site.com", "/")]
    [InlineData("//fake-site.com", "/")]
    [InlineData("/\\fake-site.com", "/")]
    public void SafeLocalUrl_OnlyAcceptsLocalUrls(string? input, string expected) =>
        Assert.Equal(expected, AuthEndpoints.SafeLocalUrl(input));
}

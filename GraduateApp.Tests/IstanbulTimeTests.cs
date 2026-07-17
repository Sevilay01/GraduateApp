using GraduateApp.Web.Models;

namespace GraduateApp.Tests;

public sealed class IstanbulTimeTests
{
    [Fact]
    public void Local_form_time_round_trips_without_calendar_shift()
    {
        var local = new DateTime(2026, 9, 1, 0, 30, 0, DateTimeKind.Unspecified);

        var utc = IstanbulTime.ToUtc(local);
        var displayed = IstanbulTime.FromUtc(utc);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(local, DateTime.SpecifyKind(displayed, DateTimeKind.Unspecified));
        Assert.Equal(new DateOnly(2026, 9, 1), DateOnly.FromDateTime(displayed));
    }
}

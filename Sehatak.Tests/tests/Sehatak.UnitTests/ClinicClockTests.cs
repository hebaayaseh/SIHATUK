using Sehatak.Application.Common;
using Shouldly;

namespace Sehatak.UnitTests;

public class ClinicClockTests
{
    [Fact]
    public void Zone_resolves_on_this_machine()
    {
        // Fails on slim CI images without tzdata (see test plan section 2, point 7).
        Should.NotThrow(() => _ = ClinicClock.Now);
    }

    [Fact]
    public void Now_is_UTC_plus_2_or_3()
    {
        var offset = ClinicClock.Now - DateTime.UtcNow;
        offset.TotalHours.ShouldBeInRange(1.99, 3.01);
    }

    [Fact]
    public void Today_matches_date_part_of_Now()
    {
        // Retry once to be safe around midnight rollover.
        var ok = ClinicClock.Today == DateOnly.FromDateTime(ClinicClock.Now)
              || ClinicClock.Today == DateOnly.FromDateTime(ClinicClock.Now);
        ok.ShouldBeTrue();
    }
}

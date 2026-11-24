using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Sehatak.IntegrationTests.Conventions;

/// <summary>
/// The first rule (ClinicTimeRulesTests) catches "today" and "now". These catch the other ways to get the wrong clinic day:
///  - a date computed from UtcNow plus days (subscription end dates, reminder start dates),
///  - the "current month / year" taken from UtcNow (default month of a schedule or a report: on the 1st, between 00:00 and 03:00
///    in Palestine, the app would show LAST month).
/// A line that really needs the UTC value can say so with a trailing comment:  // utc-ok
/// </summary>
public class ClinicTimeRulesExtraTests
{
    private static readonly Regex DateFromUtcPlusDays = new(
        @"(DateOnly|TimeOnly)\.FromDateTime\(\s*DateTime\.UtcNow\s*\.\s*Add\w+\(");

    private static readonly Regex MonthOrYearFromUtc = new(
        @"DateTime\.UtcNow\s*\.\s*(Year|Month|Day|DayOfWeek|DayOfYear)\b");

    [Fact]
    [Trait("Category", "KnownBug")]
    public void A_date_built_from_UtcNow_plus_days_uses_the_clinic_day()
    {
        var hits = SourceTree.Find(DateFromUtcPlusDays, f => Path.GetFileName(f) == "ClinicClock.cs")
            .Where(h => !h.Line.Contains("utc-ok"))
            .Select(h => $"{h.Where}:  {h.Line}").ToList();

        hits.ShouldBeEmpty("استعمل ClinicClock.Today.AddDays(...) بدل DateOnly.FromDateTime(DateTime.UtcNow.AddDays(...)):\n" + string.Join("\n", hits));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public void The_current_month_or_year_comes_from_the_clinic_clock()
    {
        var hits = SourceTree.Find(MonthOrYearFromUtc, f => Path.GetFileName(f) == "ClinicClock.cs")
            .Where(h => !h.Line.Contains("utc-ok"))
            .Select(h => $"{h.Where}:  {h.Line}").ToList();

        hits.ShouldBeEmpty("استعمل ClinicClock.Today.Year / .Month (أو علّم السطر // utc-ok لو UTC مقصود):\n" + string.Join("\n", hits));
    }

    // ---------------------------------------------------------------- the zone itself

    [Fact]
    public void The_clinic_time_zone_exists_on_this_machine_and_has_summer_time()
    {
        // on a server image without tzdata (some slim Linux containers) FindSystemTimeZoneById throws and ClinicClock - hence the app - fails
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Hebron");

        zone.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)).ShouldBe(TimeSpan.FromHours(2));
        zone.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc)).ShouldBe(TimeSpan.FromHours(3));
    }

    [Theory]
    [InlineData("2026-01-31 22:30", "2026-02-01")]   // winter: 22:30 UTC is already 00:30 next day (+2)
    [InlineData("2026-06-30 21:30", "2026-07-01")]   // summer: 21:30 UTC is already 00:30 next day (+3)
    [InlineData("2026-12-31 22:30", "2027-01-01")]   // new year arrives two hours before UTC
    [InlineData("2026-07-01 20:59", "2026-07-01")]   // summer: 20:59 UTC is 23:59, still the same day
    public void The_clinic_day_changes_before_the_utc_day(string utc, string clinicDay)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Hebron");
        var u = DateTime.SpecifyKind(DateTime.Parse(utc), DateTimeKind.Utc);

        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(u, zone)).ShouldBe(DateOnly.Parse(clinicDay));
    }
}

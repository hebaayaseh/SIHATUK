using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Sehatak.IntegrationTests.Conventions;

/// <summary>
/// Rule of the project: instants are stored as DateTime.UtcNow; "today / now AT THE CLINIC" always comes from ClinicClock.
/// Every line below gives the wrong date for the first 2-3 hours after midnight in Palestine (UTC is still yesterday).
/// A line that really needs the UTC date can say so with a trailing comment:  // utc-ok
/// </summary>
public class ClinicTimeRulesTests
{
    private static readonly Regex Forbidden = new(
        @"DateTime\.Now\b|DateTime\.Today\b|DateOnly\.FromDateTime\(\s*DateTime\.UtcNow\s*\)|DateTime\.UtcNow\.Date\b|DateTime\.UtcNow\.TimeOfDay|TimeOnly\.FromDateTime\(\s*DateTime\.UtcNow\s*\)");

    [Fact]
    [Trait("Category", "KnownBug")]
    public void Clinic_dates_and_times_come_from_ClinicClock_never_from_the_server_clock()
    {
        var hits = SourceTree.Find(Forbidden, f => Path.GetFileName(f) == "ClinicClock.cs")
            .Where(h => !h.Line.Contains("utc-ok"))
            .Select(h => $"{h.Where}:  {h.Line}")
            .ToList();

        hits.ShouldBeEmpty("بدّلها بـ ClinicClock.Today / ClinicClock.Now:\n" + string.Join("\n", hits));
    }
}

using Sehatak.Infrastructure.CalculateSlot;
using Shouldly;

namespace Sehatak.UnitTests;

public class GenerateTheoreticalSlotsTests
{
    private readonly GenerateTheoreticalSlots _sut = new();

    [Fact]
    public void Generates_back_to_back_slots_inside_the_window()
    {
        var slots = _sut.GenerateTheoreticalSlot(new TimeOnly(9, 0), new TimeOnly(10, 0), 20);

        slots.ShouldBe(new TimeOnly?[]
        {
            new TimeOnly(9, 0), new TimeOnly(9, 20), new TimeOnly(9, 40)
        });
    }

    [Fact]
    public void Last_slot_must_end_at_or_before_endTime()
    {
        // 09:00-10:00 with 25 min slots: 09:00 and 09:25 fit, 09:50 would end 10:15
        var slots = _sut.GenerateTheoreticalSlot(new TimeOnly(9, 0), new TimeOnly(10, 0), 25);

        slots.Count.ShouldBe(2);
        slots.Last().ShouldBe(new TimeOnly(9, 25));
    }

    [Fact]
    public void Slot_that_ends_exactly_at_endTime_is_included()
    {
        var slots = _sut.GenerateTheoreticalSlot(new TimeOnly(9, 0), new TimeOnly(9, 30), 30);

        slots.ShouldBe(new TimeOnly?[] { new TimeOnly(9, 0) });
    }

    [Fact]
    public void Window_shorter_than_one_slot_returns_nothing()
    {
        _sut.GenerateTheoreticalSlot(new TimeOnly(9, 0), new TimeOnly(9, 10), 30).ShouldBeEmpty();
    }

    [Fact]
    public void Start_equal_to_end_returns_nothing()
    {
        _sut.GenerateTheoreticalSlot(new TimeOnly(9, 0), new TimeOnly(9, 0), 15).ShouldBeEmpty();
    }

    [Fact]
    public void Start_after_end_returns_nothing()
    {
        _sut.GenerateTheoreticalSlot(new TimeOnly(10, 0), new TimeOnly(9, 0), 15).ShouldBeEmpty();
    }

    [Fact]
    public void Window_crossing_midnight_does_not_loop_forever()
    {
        var task = Task.Run(() =>
            _sut.GenerateTheoreticalSlot(new TimeOnly(23, 0), new TimeOnly(23, 59), 30));

        task.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue("generator did not terminate");
        task.Result.ShouldBe(new TimeOnly?[] { new TimeOnly(23, 0) });
    }

    [Fact]
    public void Schedule_ending_late_in_the_evening_stops_before_midnight()
    {
        // 23:30 + 30min would wrap to 00:00 and (before the fix) loop forever.
        var slots = _sut.GenerateTheoreticalSlot(new TimeOnly(22, 0), new TimeOnly(23, 59), 30);

        slots.ShouldBe(new TimeOnly?[] { new TimeOnly(22, 0), new TimeOnly(22, 30), new TimeOnly(23, 0) });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Non_positive_duration_must_not_hang(int minutes)
    {
        var task = Task.Run(() =>
            _sut.GenerateTheoreticalSlot(new TimeOnly(9, 0), new TimeOnly(10, 0), minutes));

        task.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue("infinite loop on non-positive slot duration");
        task.Result.ShouldBeEmpty();
    }
}
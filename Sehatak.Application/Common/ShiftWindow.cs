

namespace Sehatak.Application.Common
{
    public static class ShiftWindow
    {
        public static bool CrossesMidnight(TimeOnly start, TimeOnly end) => end <= start;

        public static (DateTime Start, DateTime End) For(DateOnly shiftDate, TimeOnly start, TimeOnly end)
        {
            var s = shiftDate.ToDateTime(start);
            var e = shiftDate.ToDateTime(end);
            return (s, end <= start ? e.AddDays(1) : e);   // 22:00→06:00 and 16:00→00:00 end the next day
        }
    }
}

namespace Sehatak.Infrastructure.CalculateSlot
{
    public class GenerateTheoreticalSlots
    {
        public List<TimeOnly?> GenerateTheoreticalSlot(TimeOnly startTime, TimeOnly endTime, int slotDurationMinutes)
        {
            var slots = new List<TimeOnly?>();

            if (slotDurationMinutes <= 0)
                return slots;

            var current = startTime.ToTimeSpan();
            var end = endTime.ToTimeSpan();
            var step = TimeSpan.FromMinutes(slotDurationMinutes);

            while (current + step <= end)
            {
                slots.Add(TimeOnly.FromTimeSpan(current));
                current += step;
            }

            return slots;
        }
    }
}
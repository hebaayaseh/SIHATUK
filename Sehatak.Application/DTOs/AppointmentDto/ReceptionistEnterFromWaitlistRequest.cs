namespace Sehatak.Application.DTOs.AppointmentDto
{
    public class ReceptionistEnterFromWaitlistRequest
    {
        public int WaitListId { get; set; }

        /// <summary>اختياري. إذا ما انبعت: أول موعد فاضي، وإذا اليوم ممتلئ: موعد بعد آخر موعد محجوز.</summary>
        public TimeOnly? TimeSlot { get; set; }
    }

    public class ReceptionistEnterFromWaitlistResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int AppointmentId { get; set; }
        public DateOnly Date { get; set; }
        public TimeOnly TimeSlot { get; set; }

        /// <summary>true = الموعد بعد آخر موعد محجوز لأن اليوم ممتلئ (أو خارج ساعات الدوام).</summary>
        public bool IsOverflow { get; set; }
        public bool CheckedIn { get; set; }
    }
}



namespace Sehatak.Application.DTOs.DoctorDailyHourDto
{
    public class GetDoctorsBlockDaysResponseDto
    {
        public int Id { get; set; }

        public int DoctorId { get; set; }
        public string DoctorName { get; set; }
        public TimeOnly? TimeSlot { get; set; }
        public DateOnly? date { get; set; } 
    }
}

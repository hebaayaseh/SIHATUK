using Sehatak.Application.DTOs.DoctorRatingDto;


namespace Sehatak.Application.Interfaces.IDoctorRating
{
    public interface IDoctorRating
    {
        Task<DoctorRatingResponse> AddDoctorRatingAsync(int centerId, int userId, AddDoctorRatingRequest request);
        Task<DoctorRatingResponse> UpdateDoctorRatingAsync(int centerId, int userId, UpdateDoctorRatingRequest request);
        Task<string> RemoveDoctorRatingAsync(int centerId, int userId , int ratingId);
        Task<List<GetMyRatingsResponse>> PatientGetRatingsAsync(int centerId , int userId);
        Task<DoctorGetRatingResponse> DoctorGetRatingsAsync(int centerId, int userId);
    }
}

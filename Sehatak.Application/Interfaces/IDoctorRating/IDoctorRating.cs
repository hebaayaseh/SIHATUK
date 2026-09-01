using Sehatak.Application.DTOs.DoctorRaitingDto;
using Sehatak.Application.DTOs.DoctorRatingDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

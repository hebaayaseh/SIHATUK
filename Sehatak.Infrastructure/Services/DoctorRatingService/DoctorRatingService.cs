using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.DoctorRatingDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.IDoctorRating;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System.Linq.Dynamic.Core;

namespace Sehatak.Infrastructure.Services.DoctorRatingService
{
    public class DoctorRatingService : IDoctorRating
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public DoctorRatingService(SharedDbContext sharedDbContext , TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }
        public async Task<DoctorRatingResponse> AddDoctorRatingAsync(int centerId, int userId, AddDoctorRatingRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var patient = await db.Patients
                .Include(u => u.user)
                .FirstOrDefaultAsync(p => p.userId == userId
                                     && p.user.isActive);

            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            var appointment = await db.Appointments
                .Include(a => a.Rating)
                .FirstOrDefaultAsync(a => a.Id == request.AppointmentId 
                                    && a.patientId == patient.patientId);

            if (appointment == null)
                throw new BusinessException("Appointment.NotFound");

            if (appointment.appointmentStatus != AppointmentStatus.Completed)
                throw new BusinessException("DoctorRating.AppointmentNotCompleted");

            if (appointment.Rating != null)
                throw new BusinessException("DoctorRating.AlreadyRated");

            var rating = new DoctorRating
            {
                DoctorId = appointment.doctorId,
                PatientId = patient.patientId,
                AppointmentId = appointment.Id,
                Rating = request.Rating,
                Review = request.Review,
                CreatedAt = DateTime.UtcNow,
                UpdateAt = DateTime.UtcNow
            };

            await db.DoctorRatings.AddAsync(rating);
            await db.SaveChangesAsync();

            
            return new DoctorRatingResponse
            {
                Id = rating.Id,
                PatientId = patient.patientId,
                PatientName = $"{patient.user.firstName} {patient.user.lastName}",
                AppointmentId = rating.AppointmentId,
                DoctorId = rating.DoctorId,
                CreatedAt = rating.CreatedAt,
                UpdateAt = rating.UpdateAt,
                Rating = rating.Rating,
                Review = rating.Review
            };
        }

        public async Task<DoctorGetRatingResponse> DoctorGetRatingsAsync(int centerId, int userId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == userId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var ratings = await db.DoctorRatings
                .Include(p => p.Patient)
                .ThenInclude(u=>u.user)
                .Where(p => p.DoctorId == doctor.Id)
                .ToListAsync();

            return new DoctorGetRatingResponse
            {
                AvrageRating = ratings.Any() ? ratings.Average(r => r.Rating) : 0,
                PatientRatings = ratings.Select(p => new PatientSummaryRating
                {
                    patientId = p.PatientId,
                    patientName = $"{p.Patient.user.firstName} {p.Patient.user.lastName}",
                    AppointmentId = p.AppointmentId,
                    Rating = p.Rating,
                    Review = p.Review
                }).ToList() 
            };

        }

        public async Task<Application.Common.PagedResult<GetMyRatingsResponse>> PatientGetRatingsAsync(int centerId,int userId, PagedRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var patient = await db.Patients
                .Include(u => u.user)
                .FirstOrDefaultAsync(p => p.userId == userId
                                     && p.user.isActive);

            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            var query = db.DoctorRatings
                .Include(r=> r.Doctor)
                .Where(r => r.PatientId == patient.patientId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new GetMyRatingsResponse
                {
                    ratingId = r.Id,
                    doctorId = r.DoctorId,
                    doctorName = $"{r.Doctor.user.firstName} {r.Doctor.user.lastName}",
                    AppointmentId = r.AppointmentId,
                    Rating = r.Rating,
                    Review = r.Review,
                    CreatedAt = r.CreatedAt,
                    UpdateAt = r.UpdateAt
                });
            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);

        }

        public async Task<string> RemoveDoctorRatingAsync(int centerId, int userId, int ratingId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var patient = await db.Patients
                .Include(u => u.user)
                .FirstOrDefaultAsync(p => p.userId == userId
                                     && p.user.isActive);

            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            var rating = await db.DoctorRatings
                .FirstOrDefaultAsync(r => r.Id == ratingId
                                     && r.PatientId == patient.patientId);

            if (rating == null)
                throw new BusinessException("Rating.NotFound");

            db.Remove(rating);
            await db.SaveChangesAsync();
            return "تم الحذف بنجاح";
        }

        public async Task<DoctorRatingResponse> UpdateDoctorRatingAsync(int centerId, int userId, UpdateDoctorRatingRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var patient = await db.Patients
                .Include(u => u.user)
                .FirstOrDefaultAsync(p => p.userId == userId
                                     && p.user.isActive);

            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            var rating = await db.DoctorRatings
                .FirstOrDefaultAsync(r => r.Id == request.RatingId
                                     && r.PatientId == patient.patientId);

            if (rating == null)
                throw new BusinessException("Rating.NotFound");

            if (request.Rating != null)
                rating.Rating = (int)request.Rating.Value;

            if(request.Review != null)
                rating.Review = request.Review;

            rating.UpdateAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return new DoctorRatingResponse
            {
                Id = rating.Id,
                PatientId = rating.PatientId,
                PatientName = $"{patient.user.firstName} {patient.user.lastName}",
                AppointmentId = rating.AppointmentId,
                Rating = rating.Rating,
                Review = rating.Review,
                CreatedAt = rating.CreatedAt,
                UpdateAt = rating.UpdateAt,
                DoctorId = rating.DoctorId
            };
        }

    }
}

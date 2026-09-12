using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.LabDto;
using Sehatak.Application.Interfaces.ILab;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.LabService
{
    public class LabService : ILab
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public LabService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<LabRequestResponseDto> CreateLabRequestAsync(int centerId, int userId, CreateLabRequestDto request)
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

            var patientExists = await db.Patients
               .Include(u => u.user)
               .FirstOrDefaultAsync(p => p.patientId == request.PatientId);

            if (patientExists == null)
                throw new BusinessException("Patient.NotFound");

            if (patientExists.userId != null && patientExists.user.isActive == false)
                throw new BusinessException("Patient.NotFound");

            var appointment = await db.Appointments
                .Include(p => p.Patient)
                .ThenInclude(u => u.user)
                .FirstOrDefaultAsync(a => a.Id == request.AppointmentId
                                     && a.patientId == request.PatientId
                                     && a.doctorId == doctor.Id
                                     && a.appointmentStatus != AppointmentStatus.Cancelled);

            if (appointment == null)
                throw new BusinessException("Appointment.NotFound");

            var labRequest = new LabRequest
            {
                AppointmentId = request.AppointmentId,
                PatientId = request.PatientId,
                DoctorId = doctor.Id,
                RequstedAt = DateTime.UtcNow,
                Notes = request.Note,
                Status = LabRequestStatus.Pending,
            };

            await db.LabRequests.AddAsync(labRequest);

            var responseItems = new List<LabItemResponseDto>();
            if (request.LabItems != null)
            {
                var seenIds = new HashSet<int>();
                foreach (var Item in request.LabItems)
                {
                    if (!seenIds.Add(Item.Id))
                        throw new BusinessException("LabRequest.DuplicateItem");

                    var servicePrice = await db.ServicePrices
                        .FirstOrDefaultAsync(s => s.Id == Item.Id
                                             && s.Type == ServiceType.LabTest
                                             && s.IsActive);

                    if (servicePrice == null)
                        throw new BusinessException("ServicePrice.NotFound");

                    var item = new LabRequestItem
                    {
                        ServicePriceId = Item.Id,
                        LabRequest = labRequest,          
                        UnitPrice = servicePrice.Price,
                    };
                    await db.LabRequestItems.AddAsync(item);

                    responseItems.Add(new LabItemResponseDto
                    {
                        ServicePriceId = servicePrice.Id,
                        ServiceName = servicePrice.ServiceName,
                        UnitPrice = servicePrice.Price,
                    });
                }
            }

            await db.SaveChangesAsync();
            var grandTotal = responseItems.Sum(i => i.UnitPrice);

            return new LabRequestResponseDto
            {
                LabRequestId = labRequest.Id,
                PatientId = request.PatientId,
                PatientName = patientExists.userId != null
                ? patientExists.user.firstName + " " + patientExists.user.lastName
                : patientExists.FirstName + " " + patientExists.LastName,
                AppointmentId = request.AppointmentId,
                CreatedAt = labRequest.RequstedAt,
                LabItems = responseItems,
                TotalPrice = grandTotal,
                LabStatus = labRequest.Status.ToString(),
                Note = labRequest.Notes,
                UpdatedAt = DateTime.UtcNow
            };
        }

        public async Task<PagedResult<LabRequestResponseDto>> GetLabRequestForPatientAsync(int centerId, int userId, int patientId, PagedRequest request)
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

            var patientExists = await db.Patients
               .Include(u => u.user)
               .FirstOrDefaultAsync(p => p.patientId == patientId);

            if (patientExists == null)
                throw new BusinessException("Patient.NotFound");

            if (patientExists.userId != null && patientExists.user.isActive == false)
                throw new BusinessException("Patient.NotFound");

            var query = db.LabRequests
                .Where(l => l.PatientId == patientId)
                .OrderByDescending(r => r.RequstedAt)
                .Select(n => new LabRequestResponseDto
                {
                    LabRequestId = n.Id,
                    AppointmentId = (int)n.AppointmentId,
                    PatientId = n.PatientId,
                    PatientName = patientExists.userId != null
                    ? patientExists.user.firstName + " " + patientExists.user.lastName
                    : patientExists.FirstName + " " + patientExists.LastName,
                    CreatedAt = n.RequstedAt,
                    LabStatus = n.Status.ToString(),
                    Note = n.Notes,
                    LabItems = n.Items.Select(i => new LabItemResponseDto
                    {
                        ServicePriceId = i.ServicePriceId,
                        ServiceName = i.ServicePrice.ServiceName,
                        LabRequestId = i.LabRequestId,
                        UnitPrice = i.UnitPrice
                    }).ToList(),
                    TotalPrice = n.Items.Sum(i => i.UnitPrice)
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<PagedResult<PatientGetLabRequestReponseDto>> PatientGetLabRequestAsync(int centerId, int userId, PagedRequest request,int?subPatientId)
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


            Patient actingPatient = patient;

            if (subPatientId.HasValue)
            {
                var subPatient = await db.Patients
                    .FirstOrDefaultAsync(s => s.patientId == subPatientId.Value
                                         && s.ParentPatientId == patient.patientId);

                if (subPatient == null)
                    throw new BusinessException("SubPatient.NotFoundOrNotOwned");

                actingPatient = subPatient;
            }

            var query = db.LabRequests
                .Where(l => l.PatientId == actingPatient.patientId)
                .OrderByDescending(r => r.RequstedAt)
                .Select(n => new PatientGetLabRequestReponseDto
                {
                    LabRequestId = n.Id,
                    CreatedAt = n.RequstedAt,
                    LabStatus = n.Status.ToString(),
                    Note = n.Notes,
                    LabItems = n.Items.Select(i => new LabItemResponseDto
                    {
                        ServicePriceId = i.ServicePriceId,
                        ServiceName = i.ServicePrice.ServiceName,
                        LabRequestId = i.LabRequestId,
                        UnitPrice = i.UnitPrice
                    }).ToList(),
                    TotalPrice = n.Items.Sum(i => i.UnitPrice)
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }


        public async Task<LabRequestResponseDto> UpdateLabRequestAsync(int centerId, int userId, UpdateLabRequestDto request)
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

            var patientExists = await db.Patients
               .Include(u => u.user)
               .FirstOrDefaultAsync(p => p.patientId == request.PatientId);

            if (patientExists == null)
                throw new BusinessException("Patient.NotFound");

            if (patientExists.userId != null && patientExists.user.isActive == false)
                throw new BusinessException("Patient.NotFound");

            var appointment = await db.Appointments
                .Include(p => p.Patient)
                .ThenInclude(u => u.user)
                .FirstOrDefaultAsync(a => a.Id == request.AppointmentId
                                     && a.patientId == request.PatientId
                                     && a.doctorId == doctor.Id
                                     && a.appointmentStatus != AppointmentStatus.Cancelled);

            if (appointment == null)
                throw new BusinessException("Appointment.NotFound");

            var labRequest = await db.LabRequests
                .FirstOrDefaultAsync(l => l.Id == request.LabRequestId
                                     && l.AppointmentId == appointment.Id
                                     && l.Status == LabRequestStatus.Pending);

            if (labRequest == null)
                throw new BusinessException("LabRequest.NotFound");

            if (request.Note != null)
                labRequest.Notes = request.Note;

            var LabItems = await db.LabRequestItems
                .Where(l => l.LabRequestId == request.LabRequestId)
                .ToListAsync();

            if (request.RemoveLabItems != null && LabItems != null)
            {
                foreach (var itemId in request.RemoveLabItems)
                {
                    var removeItem = LabItems.FirstOrDefault(l => l.Id == itemId);
                    if (removeItem != null)
                    {
                        db.LabRequestItems.Remove(removeItem);
                    }
                }
            }

            if (request.AddLabItems != null)
            {
                var existingIds = LabItems
                        .Where(l => request.RemoveLabItems == null 
                               || !request.RemoveLabItems.Contains(l.Id))
                        .Select(l => l.ServicePriceId)
                        .ToHashSet();

                foreach (var Item in request.AddLabItems)
                {
                    if (!existingIds.Add(Item.Id))
                        throw new BusinessException("LabRequest.DuplicateItem");

                    var servicePrice = await db.ServicePrices
                        .FirstOrDefaultAsync(s => s.Id == Item.Id
                                             && s.Type == ServiceType.LabTest
                                             && s.IsActive);

                    if (servicePrice == null)
                        throw new BusinessException("ServicePrice.NotFound");

                    var item = new LabRequestItem
                    {
                        ServicePriceId = Item.Id,
                        LabRequestId = labRequest.Id,   
                        UnitPrice = servicePrice.Price,
                    };
                    await db.LabRequestItems.AddAsync(item);
                }
            }

            await db.SaveChangesAsync();

            var currentItems = await db.LabRequestItems
                .Include(i => i.ServicePrice)
                .Where(i => i.LabRequestId == labRequest.Id)
                .Select(i => new LabItemResponseDto
                {
                    ServicePriceId = i.ServicePriceId,
                    ServiceName = i.ServicePrice.ServiceName,
                    LabRequestId = i.LabRequestId,
                    UnitPrice = i.UnitPrice
                })
                .ToListAsync();

            var grandTotal = currentItems.Sum(i => i.UnitPrice);

            return new LabRequestResponseDto
            {
                LabRequestId = labRequest.Id,
                PatientId = request.PatientId,
                PatientName = patientExists.userId != null
                ? patientExists.user.firstName + " " + patientExists.user.lastName
                : patientExists.FirstName + " " + patientExists.LastName,
                AppointmentId = request.AppointmentId,
                CreatedAt = labRequest.RequstedAt,
                LabStatus = labRequest.Status.ToString(),
                LabItems = currentItems,
                TotalPrice = grandTotal,
                Note = labRequest.Notes,   
                UpdatedAt = DateTime.UtcNow
            };
        }
    }
}
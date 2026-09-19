using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.LabDto;
using Sehatak.Application.Interfaces.ILab;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
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

        public async Task<string> CancleLabReqquestAsync(int centerId, int userId, int labRequestId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                     && u.isActive);

            if (user == null)
                throw new BusinessException("User.NotFound");

            var labRequest = await db.LabRequests
                .FirstOrDefaultAsync(l => l.Id == labRequestId
                                     && l.RequestedByUserId == userId
                                     && (l.Status != LabRequestStatus.Collected
                                     && l.Status != LabRequestStatus.Completed));

            if (labRequest == null)
                throw new BusinessException("LabRequest.NotFound");

            labRequest.Status = LabRequestStatus.Cancelled;
            labRequest.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return "تم الغاء الطلب بنجاح.";
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
                RequestedByUserId = userId,
                UpdatedAt = DateTime.UtcNow

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
                        ItemId = item.Id
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
                UpdatedAt = labRequest.UpdatedAt
            };
        }

        public async Task<GetLabResultDto> DoctorGetLabResultsForPatient(int centerId, int userId, int patientId, int labRequestId)
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

            var query = await db.LabRequests
                .Where(l => l.PatientId == patientId
                       && l.Id == labRequestId
                       && l.Status == LabRequestStatus.Completed)
                .Select(n => new GetLabResultDto
                {
                    LabRequestId = n.Id,
                    AppointmentId = n.AppointmentId,
                    PatientId = n.PatientId,
                    PatientName = patientExists.userId != null
                    ? patientExists.user.firstName + " " + patientExists.user.lastName
                    : patientExists.FirstName + " " + patientExists.LastName,
                    CreatedAt = n.RequstedAt,
                    Note = n.Notes,
                    LabItems = n.Items.Select(i => new LabResultItemResponseDto
                    {
                        ServicePriceId = i.ServicePriceId,
                        ServicePriceName = i.ServicePrice.ServiceName,
                        LabRequestItemId = i.Id,
                        ResultValue = i.ResultValue,
                        ResultFileUrl = i.ResultFileUrl
                    }).ToList(),
                }).FirstOrDefaultAsync();
            if (query == null)
                throw new BusinessException("LabResult.NotFound");
            return query;
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
                    AppointmentId = n.AppointmentId,
                    PatientId = n.PatientId,
                    PatientName = patientExists.userId != null
                    ? patientExists.user.firstName + " " + patientExists.user.lastName
                    : patientExists.FirstName + " " + patientExists.LastName,
                    CreatedAt = n.RequstedAt,
                    UpdatedAt = n.UpdatedAt,
                    LabStatus = n.Status.ToString(),
                    Note = n.Notes,
                    LabItems = n.Items.Select(i => new LabItemResponseDto
                    {
                        ServicePriceId = i.ServicePriceId,
                        ServiceName = i.ServicePrice.ServiceName,
                        ItemId = i.Id,
                        UnitPrice = i.UnitPrice
                    }).ToList(),
                    TotalPrice = n.Items.Sum(i => i.UnitPrice)
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<string> LabCollectSample(int centerId, int userId, int labRequestId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var technician = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                     && u.isActive);

            if (technician == null)
                throw new BusinessException("Technician.NotFound");

            var labRequest = await db.LabRequests
                .FirstOrDefaultAsync(l => l.Id == labRequestId);

            if (labRequest == null)
                throw new BusinessException("LabRequest.NotFound");

            if (labRequest.Payment == null)
                throw new BusinessException("Payment.NotCompleted");

            if (labRequest.Status != LabRequestStatus.Pending
                && labRequest.Status != LabRequestStatus.Seen)
                throw new BusinessException("LabRequest.AlreadyCollected");

            labRequest.Status = LabRequestStatus.Collected;
            labRequest.UpdatedAt = DateTime.UtcNow;


            await db.SaveChangesAsync();

            return "Collected";
        }

        public async Task<PagedResult<LabGetRequestResponseDto>> LabGetPendingRequestsAsync(int centerId, int userId, PagedRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var technician = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                     && u.isActive);

            if (technician == null)
                throw new BusinessException("Technician.NotFound");

            var query = db.LabRequests
                .Where(l => l.Status == LabRequestStatus.Pending
                        || l.Status == LabRequestStatus.Seen
                        || l.Status == LabRequestStatus.Collected)
                .OrderByDescending(c => c.RequstedAt)
                .Select(n => new LabGetRequestResponseDto
                {
                    LabRequestId = n.Id,
                    LabStatus = n.Status.ToString(),
                    Note = n.Notes,
                    PatientId = n.PatientId,
                    PatientName = n.Patient.userId != null
                        ? n.Patient.user.firstName + " " + n.Patient.user.lastName
                        : n.Patient.FirstName + " " + n.Patient.LastName,
                    CreatedAt = n.RequstedAt
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<LabGetRequestResponseDto> labGetRequestAsync(int centerId, int userId, int labRequestId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var technician = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                     && u.isActive);

            if (technician == null)
                throw new BusinessException("Technician.NotFound");

            var labRequest = await db.LabRequests
                .Include(p => p.Patient)
                .ThenInclude(u => u.user)
                .FirstOrDefaultAsync(l => l.Id == labRequestId);

            if (labRequest == null)
                throw new BusinessException("LabRequest.NotFound");

            var items = await db.LabRequestItems
                .Include(s => s.ServicePrice)
                .Where(l => l.LabRequestId == labRequestId)
                .ToListAsync();

            return new LabGetRequestResponseDto
            {
                LabRequestId = labRequestId,
                LabStatus = labRequest.Status.ToString(),
                Note = labRequest.Notes,
                PatientId = labRequest.PatientId,
                PatientName = labRequest.Patient.userId != null
                    ? labRequest.Patient.user.firstName + " " + labRequest.Patient.user.lastName
                    : labRequest.Patient.FirstName + " " + labRequest.Patient.LastName,
                CreatedAt = labRequest.RequstedAt,
                LabItems = items.Select(n => new LabResultItemResponseDto
                {
                    LabRequestItemId = n.Id,
                    ServicePriceId = n.ServicePriceId,
                    ServicePriceName = n.ServicePrice.ServiceName,
                    ResultValue = n.ResultValue,
                    ResultFileUrl = n.ResultFileUrl
                }).ToList(),
                TotalPrice = items.Sum(x => x.UnitPrice)
            };
        }

        public async Task<LabUploadResultResponseDto> LabUploadResult(int centerId, int userId, UploadLabResultRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var technician = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                     && u.isActive);

            if (technician == null)
                throw new BusinessException("Technician.NotFound");

            var labRequest = await db.LabRequests
                .Include(l => l.Patient)
                .FirstOrDefaultAsync(l => l.Id == request.LabRequestId);

            if (labRequest == null)
                throw new BusinessException("LabRequest.NotFound");

            if (labRequest.Status != LabRequestStatus.Collected)
                throw new BusinessException("LabRequest.NotCollected");

            var payment = await db.Payments
                .FirstOrDefaultAsync(p => p.LabRequestId == request.LabRequestId
                                     && p.Status == PaymentStatus.Paid);

            if (payment == null)
                throw new BusinessException("Payment.NotCompleted");

            foreach (var item in request.Results)
            {
                var Item = await db.LabRequestItems
                    .FirstOrDefaultAsync(l => l.LabRequestId == labRequest.Id
                                         && l.Id == item.LabRequestItemId);

                if (Item == null)
                    throw new BusinessException("LabRequestItemNotFound");

                Item.ResultValue = item.ResultValue;

                string? result = null;
                if (item.ResultFileUrl != null)
                {
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
                    var extension = Path.GetExtension(item.ResultFileUrl.FileName).ToLower();
                    if (!allowedExtensions.Contains(extension))
                        throw new BusinessException("Validation.InvalidFileType");

                    if (item.ResultFileUrl.Length > 5 * 1024 * 1024)
                        throw new BusinessException("Validation.FileTooLarge");

                    var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    var uploadsFolder = Path.Combine(webRoot, "uploads", "labResult");
                    Directory.CreateDirectory(uploadsFolder);

                    var fileName = $"{Guid.NewGuid()}{extension}";
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    using var stream = new FileStream(filePath, FileMode.Create);
                    await item.ResultFileUrl.CopyToAsync(stream);

                    result = $"/uploads/labResult/{fileName}";
                }
                Item.ResultFileUrl = result;
            }

            await db.SaveChangesAsync();

            var stillPending = await db.LabRequestItems
                .AnyAsync(l => l.LabRequestId == labRequest.Id && l.ResultValue == null);

            if (!stillPending)
            {
                var labResult = new LabResult
                {
                    LabRequestId = labRequest.Id,
                    PatientId = labRequest.Patient.patientId,
                    TechnicianId = userId,
                    PaymentId = payment.Id,
                    Status = LabStatus.Delivered
                };
                await db.LabResults.AddAsync(labResult);
                labRequest.Status = LabRequestStatus.Completed;

                await db.Notifications.AddAsync(new Notification
                {
                    UserId = labRequest.Patient.NotifiableUserId,
                    Message = "تم رفع نتائج التحليل بالكامل.",
                    Type = NotificationType.LabResult,
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false
                });

                labRequest.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            var currentItems = await db.LabRequestItems
                .Include(i => i.ServicePrice)
                .Where(i => i.LabRequestId == labRequest.Id)
                .Select(i => new LabResultItemResponseDto
                {
                    LabRequestItemId = i.Id,
                    ServicePriceId = i.ServicePriceId,
                    ServicePriceName = i.ServicePrice.ServiceName,
                    ResultValue = i.ResultValue,
                    ResultFileUrl = i.ResultFileUrl
                })
                .ToListAsync();

            return new LabUploadResultResponseDto
            {
                LabRequestId = labRequest.Id,
                LabStatus = labRequest.Status.ToString(),
                LabItems = currentItems
            };
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
                    UpdatedAt = n.UpdatedAt,
                    LabStatus = n.Status.ToString(),
                    PatientId = n.PatientId,
                    PatientName = actingPatient.userId != null
                    ?actingPatient.user.firstName + " " + actingPatient.user.lastName
                    : actingPatient.FirstName + " " + actingPatient.LastName,
                    Note = n.Notes,
                    LabItems = n.Items.Select(i => new LabItemResponseDto
                    {
                        ServicePriceId = i.ServicePriceId,
                        ServiceName = i.ServicePrice.ServiceName,
                        ItemId = i.Id,
                        UnitPrice = i.UnitPrice
                    }).ToList(),
                    TotalPrice = n.Items.Sum(i => i.UnitPrice)
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<PagedResult<PatientGetLabResultReponseDto>> PatientGetLabResultAsync(int centerId, int userId, PagedRequest request, int? subPatientId)
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
                .Where(p => p.PatientId == actingPatient.patientId
                       && p.Status == LabRequestStatus.Completed)
                .OrderByDescending(c => c.RequstedAt)
                .Select(n => new PatientGetLabResultReponseDto
                {
                    LabRequestId = n.Id,
                    labResultItem = n.Items
                    .Where(l => l.LabRequestId == n.Id)
                    .Select(n => new LabResultItemResponseDto
                    {
                        LabRequestItemId = n.Id,
                        ResultValue = n.ResultValue,
                        ResultFileUrl = n.ResultFileUrl,
                        ServicePriceName = n.ServicePrice.ServiceName,
                        ServicePriceId = n.ServicePriceId
                    }).ToList()
                });

            return await query.ToPagedResultAsync(request.PageNumber,request.PageSize);
        }

        public async Task<ReceptionistLabRequestReponseDto> ReceptionistCreateLabRequestAsync(int centerId, int userId, ReceptionistCreateLabRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var Receptionist = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                     && u.isActive);

            if (Receptionist == null)
                throw new BusinessException("Receptionist.NotFound");

            var patientExists = await db.Patients
              .Include(u => u.user)
              .FirstOrDefaultAsync(p => p.patientId == request.PatientId);

            if (patientExists == null)
                throw new BusinessException("Patient.NotFound");

            if (patientExists.userId != null && patientExists.user.isActive == false)
                throw new BusinessException("Patient.NotFound");

            var labRequest = new LabRequest
            {
                PatientId = patientExists.patientId,
                RequstedAt = DateTime.UtcNow,
                Notes = request.Note,
                Status = LabRequestStatus.Pending,
                RequestedByUserId = userId,
                UpdatedAt = DateTime.UtcNow
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
                        ItemId = item.Id
                    });
                }
            }

            await db.SaveChangesAsync();
            var grandTotal = responseItems.Sum(i => i.UnitPrice);

            return new ReceptionistLabRequestReponseDto
            {
                LabRequestId = labRequest.Id,
                PatientId = patientExists.patientId,
                PatientName = patientExists.userId != null
                ? patientExists.user.firstName + " " + patientExists.user.lastName
                : patientExists.FirstName + " " + patientExists.LastName,
                CreatedAt = labRequest.RequstedAt,
                LabItems = responseItems,
                TotalPrice = grandTotal,
                LabStatus = labRequest.Status.ToString(),
                Note = labRequest.Notes,
                UpdatedAt = DateTime.UtcNow
            };
        }

        public async Task<PagedResult<LabGetRequestResponseDto>> ReceptionistGetLabRequestsAwaitingPaymentAsync(int centerId, int userId, PagedRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var receptionist = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                     && u.isActive);

            if (receptionist == null)
                throw new BusinessException("Receptionist.NotFound");

            var query = db.LabRequests
                .Where(l => l.Status == LabRequestStatus.Collected)
                .OrderByDescending(l => l.RequstedAt)
                .Select(n => new LabGetRequestResponseDto
                {
                    LabRequestId = n.Id,
                    LabStatus = n.Status.ToString(),
                    Note = n.Notes,
                    PatientId = n.PatientId,
                    PatientName = n.Patient.userId != null
                        ? n.Patient.user.firstName + " " + n.Patient.user.lastName
                        : n.Patient.FirstName + " " + n.Patient.LastName,
                    CreatedAt = n.RequstedAt,
                    LabItems = n.Items.Select(i => new LabResultItemResponseDto
                    {
                        LabRequestItemId = i.Id,
                        ServicePriceId = i.ServicePriceId,
                        ServicePriceName = i.ServicePrice.ServiceName,
                        ResultValue = i.ResultValue,
                        ResultFileUrl = i.ResultFileUrl
                    }).ToList(),
                    TotalPrice = n.Items.Sum(i => i.UnitPrice)
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<ReceptionistLabRequestReponseDto> ReceptionistUpdateLabRequestAsync(int centerId, int userId, ReceptionistUpdateLabRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var Receptionist = await db.Users
                .FirstOrDefaultAsync(d => d.Id == userId
                                     && d.isActive);

            if (Receptionist == null)
                throw new BusinessException("Receptionist.NotFound");

            var patientExists = await db.Patients
               .Include(u => u.user)
               .FirstOrDefaultAsync(p => p.patientId == request.PatientId);

            if (patientExists == null)
                throw new BusinessException("Patient.NotFound");

            if (patientExists.userId != null && patientExists.user.isActive == false)
                throw new BusinessException("Patient.NotFound");

            var labRequest = await db.LabRequests
                .FirstOrDefaultAsync(l => l.Id == request.LabRequestId
                                     && l.PatientId == request.PatientId
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
            labRequest.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var currentItems = await db.LabRequestItems
                .Include(i => i.ServicePrice)
                .Where(i => i.LabRequestId == labRequest.Id)
                .Select(i => new LabItemResponseDto
                {
                    ServicePriceId = i.ServicePriceId,
                    ServiceName = i.ServicePrice.ServiceName,
                    ItemId = i.Id,
                    UnitPrice = i.UnitPrice
                })
                .ToListAsync();

            var grandTotal = currentItems.Sum(i => i.UnitPrice);

            return new ReceptionistLabRequestReponseDto
            {
                LabRequestId = labRequest.Id,
                PatientId = request.PatientId,
                PatientName = patientExists.userId != null
                ? patientExists.user.firstName + " " + patientExists.user.lastName
                : patientExists.FirstName + " " + patientExists.LastName,
                CreatedAt = labRequest.RequstedAt,
                LabStatus = labRequest.Status.ToString(),
                LabItems = currentItems,
                TotalPrice = grandTotal,
                Note = labRequest.Notes,
                UpdatedAt = labRequest.UpdatedAt
            };
        
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
            labRequest.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var currentItems = await db.LabRequestItems
                .Include(i => i.ServicePrice)
                .Where(i => i.LabRequestId == labRequest.Id)
                .Select(i => new LabItemResponseDto
                {
                    ServicePriceId = i.ServicePriceId,
                    ServiceName = i.ServicePrice.ServiceName,
                    ItemId = i.Id,
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
                UpdatedAt = labRequest.UpdatedAt
            };
        }
    }
}
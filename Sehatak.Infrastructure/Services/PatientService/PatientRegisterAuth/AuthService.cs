using BCrypt.Net;
using Castle.Core.Smtp;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using Sehatak.Application.DTOs.PatienRegisterDto;
using Sehatak.Application.DTOs.PatientLoginDto;
using Sehatak.Application.Interfaces.AuthPatient;
using Sehatak.Application.Interfaces.IAuth;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Entities;
using Sehatak.Domain.Entities.General;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Volo.Abp;


namespace Sehatak.Infrastructure.Services.PatientService.PatientRegisterAuth;
    public class AuthService : IAuth
    {
    private readonly SharedDbContext sharedDbContext;
    private readonly TenantDbContextFactory tenantFactory;
    private readonly IEmailService emailSender;
    private readonly ITokenService tokenService;
    public AuthService(SharedDbContext sharedDbContext , TenantDbContextFactory tenantDbContextFactory, IEmailService emailSenderService,ITokenService tokenService)
    {
        this.sharedDbContext = sharedDbContext;
        tenantFactory = tenantDbContextFactory;
        emailSender = emailSenderService;
        this.tokenService = tokenService;
    }

    public async Task<PatientResponseDto> LoginPatientAsync(int centerId, PatientRequestDto request)
    {
        var center = await sharedDbContext.MedicalCenters
            .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
        if (center == null)
            throw new BusinessException("Center.NotFound");

        using var db = tenantFactory.CreateForCenter(centerId);

        var patient = await db.Users
            .FirstOrDefaultAsync(p => p.isActive && p.email == request.email);

        if(patient == null )
            throw new BusinessException("Auth.Unauthorized");

        if(patient.role != userRole.Patient)
            throw new BusinessException("Auth.Forbidden");

        var passwordValid = BCrypt.Net.BCrypt.Verify(request.password, patient.passwordHash);

        if (!passwordValid)
            throw new BusinessException("Validation.PasswordMismatch");

        var tokens = await tokenService.IssueTokensAsync(
            userId: patient.Id,
            name:$"{patient.firstName} {patient.lastName}",
            email: patient.email,
            role: patient.role.ToString(),
            centerId:centerId,
            ownerType:TokenOwnerType.TenantUser
        );
        return new PatientResponseDto
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken
        };

    }

    public async Task<RegisterResponseDto> RegisterAsync(int centerId, RegisterRequestDto request)
    {
        var center = await sharedDbContext.MedicalCenters
            .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
        if (center == null)
            throw new BusinessException("Center.NotFound");

        var db = tenantFactory.CreateForCenter(centerId);
        var existing = await db.Users.FirstOrDefaultAsync(u => u.email == request.email);

        if (existing != null && existing.isActive)
            throw new BusinessException("Auth.EmailExists");

        User user;

        if (existing != null && !existing.isActive)
        {

            existing.firstName = request.firstName;
            existing.lastName = request.lastName;
            existing.passwordHash = BCrypt.Net.BCrypt.HashPassword(request.password);
            existing.phoneNumber = request.phoneNumber;
            existing.address = request.address;
            existing.city = request.city;
            

            if (request.ProfileImage != null)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(request.ProfileImage.FileName);
                var path = Path.Combine("wwwroot/uploads/receipts", fileName);
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await request.ProfileImage.CopyToAsync(stream);
                }
                existing.ProfileImageUrl = $"/uploads/receipts/{fileName}";
            }

            user = existing;
            await db.SaveChangesAsync();
        }
        else
        {
            user = new User
            {
                firstName = request.firstName,
                lastName = request.lastName,
                email = request.email,
                passwordHash = BCrypt.Net.BCrypt.HashPassword(request.password),
                phoneNumber = request.phoneNumber,
                address = request.address,
                city = request.city,
                role = userRole.Patient,
                isActive = false,
                createdAt = DateTime.UtcNow
            };

            if (request.ProfileImage != null)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(request.ProfileImage.FileName);
                var path = Path.Combine("wwwroot/uploads/profileImage", fileName);
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await request.ProfileImage.CopyToAsync(stream);
                }
                user.ProfileImageUrl = $"/uploads/profileImage/{fileName}";
            }

            await db.Users.AddAsync(user);
            await db.SaveChangesAsync();
        }
        var existingPatient = await db.Patients.FirstOrDefaultAsync(p => p.userId == user.Id);

        if (existingPatient != null)
        {
            existingPatient.DateOfBith = request.DateOfBith;
            existingPatient.BloodType = request.bloodType;
            existingPatient.Gender = request.gender;
            existingPatient.WhatsappNumber = request.phoneNumber;
        }
        else
        {
            var patient = new Patient
            {
                userId = user.Id,
                DateOfBith = request.DateOfBith,
                BloodType = request.bloodType,
                Gender = request.gender,
                WhatsappNumber = request.phoneNumber,
            };
            await db.Patients.AddAsync(patient);
        }

        await db.SaveChangesAsync();

        var code = new Random().Next(100000, 999999).ToString();
        db.EmailVerificationCodes.Add(new EmailVerificationCode
        {
            UserId = user.Id,
            Code = code,
            Purpose = "register",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await db.SaveChangesAsync();

        await emailSender.SendOtpAsync(user.email!, code, "register");

        return new RegisterResponseDto
        {
            UserId = user.Id,
            Email = user.email!
        };
    }

    public async Task<VerifyOtpResponseDto?> VerifyOtpAsync(int CenterId,VerifyOtpRequestDto request)
    {
        using var db = tenantFactory.CreateForCenter(CenterId);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId);
        if (user == null) return null;

        var validCode = await db.EmailVerificationCodes
            .Where(c => c.UserId == user.Id
                     && c.Code == request.code
                     && !c.IsUsed
                     && c.ExpiresAt > DateTime.UtcNow)
            .FirstOrDefaultAsync();

        if (validCode == null) return null;

        user.isActive = true;
        validCode.IsUsed = true;
        await db.SaveChangesAsync();



        var tokens = await tokenService.IssueTokensAsync(
            userId: user.Id,
            name: $"{user.firstName} {user.lastName}",
            email: user.email!,
            role: user.role.ToString(),
            centerId: CenterId,
            ownerType: TokenOwnerType.TenantUser
        );

        return new VerifyOtpResponseDto 
        { 
            AccessToken = tokens.AccessToken, 
            RefreshToken = tokens.RefreshToken 
        };
    }
}





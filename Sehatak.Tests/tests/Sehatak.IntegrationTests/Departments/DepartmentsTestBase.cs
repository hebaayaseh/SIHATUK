using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Sehatak.Application.DTOs.DepartmentDto;
using Sehatak.Application.DTOs.StaffSignup;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using DeptSvc = Sehatak.Infrastructure.Services.DepartmentService.DepartmentsService;
using StaffSvc = Sehatak.Infrastructure.Services.AddStaff.StaffService;

namespace Sehatak.IntegrationTests.Departments;

/// <summary>
/// Center (+ admin + department "General" + a doctor/patient from the base) and factories for
/// DepartmentsService / StaffService with a mocked email sender and ONE shared memory cache per test
/// (like the singleton IMemoryCache in the app).
/// </summary>
public abstract class DepartmentsTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private static int _seq;

    protected IEmailService Email { get; } = Substitute.For<IEmailService>();
    protected IMemoryCache Cache { get; } = new MemoryCache(new MemoryCacheOptions());

    protected DeptSvc NewDepartments() => new(Fx.Harness.TenantFactory, Fx.Harness.NewSharedDb(), Email, Cache, C.Audit);
    protected StaffSvc NewStaff() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, Email, C.Audit);

    protected static DepartmentRequestDto Dept(string name, string? description = null) =>
        new() { departmentName = name, departmentDescription = description };

    protected static string NewEmail() => $"new{Interlocked.Increment(ref _seq)}.{Guid.NewGuid():N}@test.local";

    protected DoctorRequestDto DoctorReq(string? email = null, int? departmentId = null) => new()
    {
        doctorFirstName = "Sami", doctorLastName = "Hroub", email = email ?? NewEmail(),
        phoneNumber = "0591234567", address = "addr", city = "Hebron",
        Specialization = "Cardiology", Bio = "bio", OnlineEnabled = true,
        departmentId = departmentId ?? C.DepartmentId
    };

    protected static AddStaffRequestDto StaffReq(userRole role, string? email = null) => new()
    {
        userRole = role, FirstName = "Ali", LastName = "Staff", email = email ?? NewEmail(),
        phoneNumber = "0597654321", address = "addr", city = "Hebron"
    };

    protected async Task<int> AddDepartmentRowAsync(string name)
    {
        await using var db = Db();
        var d = new Department { Name = name };
        db.Departments.Add(d);
        await db.SaveChangesAsync();
        return d.Id;
    }

    protected async Task<User?> UserByEmailAsync(string email)
    {
        await using var db = Db();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.email == email);
    }
}

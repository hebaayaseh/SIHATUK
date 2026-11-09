using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using SearchSvc = Sehatak.Infrastructure.Services.SearchDoctorService.searchDoctorService;
using StaffSvc = Sehatak.Infrastructure.Services.GetStaff.GetDoctorService;

namespace Sehatak.IntegrationTests.DoctorSearch;

public abstract class DoctorSearchTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    protected SearchSvc NewSearch() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory);
    protected StaffSvc NewStaff() => new(Fx.Harness.TenantFactory, Fx.Harness.NewSharedDb());

    protected async Task<DoctorInfo> AddNamedDoctorAsync(string first, string last, string spec = "GP", bool active = true, int? departmentId = null)
    {
        var d = await C.AddDoctorAsync(active);
        await using var db = Db();
        var u = await db.Users.SingleAsync(x => x.Id == d.UserId);
        u.firstName = first;
        u.lastName = last;
        var doc = await db.Doctors.SingleAsync(x => x.Id == d.DoctorId);
        doc.Specialization = spec;
        if (departmentId.HasValue) doc.departmentId = departmentId.Value;
        await db.SaveChangesAsync();
        return d;
    }

    protected async Task<int> AddDepartmentAsync(string name)
    {
        await using var db = Db();
        var d = new Department { Name = name };
        db.Departments.Add(d);
        await db.SaveChangesAsync();
        return d.Id;
    }

    protected async Task AddBlockAsync(int doctorId, DateOnly date, TimeOnly? slot, bool isBlocked = true)
    {
        await using var db = Db();
        db.DoctorBlockedDays.Add(new DoctorBlockedDay { doctorId = doctorId, date = date, timeSlot = slot, isBlocked = isBlocked });
        await db.SaveChangesAsync();
    }
}

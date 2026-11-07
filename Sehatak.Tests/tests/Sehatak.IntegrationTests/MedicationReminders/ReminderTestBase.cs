using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.MedicationReminderDto;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using Svc = Sehatak.Infrastructure.Services.MedicationReminderService.MedicationReminderService;

namespace Sehatak.IntegrationTests.MedicationReminders;

public abstract class ReminderTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    protected static DateOnly Today => ClinicClock.Today;
    protected Svc NewSvc() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory);

    protected static MedicationReminderRequestDto Req(string name = "Panadol", TimeOnly? time = null, bool daily = false,
        ReminderDays days = ReminderDays.Monday, int duration = 5) =>
        new() { MedicationName = name, ReminderTime = time ?? new TimeOnly(8, 0), IsDaily = daily, SelectedDays = days, DurationInDays = duration };

    protected async Task<int> SeedAsync(int patientId, TimeOnly time, ReminderDays days = ReminderDays.All,
        DateOnly? start = null, DateOnly? end = null, bool active = true, DateOnly? lastNotified = null, string name = "Aspirin")
    {
        await using var db = Db();
        var r = new MedicationReminder
        {
            PatientId = patientId, MedicationName = name, ReminderTime = time, Days = days,
            StartDate = start ?? Today.AddDays(-1), EndDate = end ?? Today.AddDays(5),
            IsActive = active, LastNotifiedDate = lastNotified
        };
        db.MedicationReminders.Add(r);
        await db.SaveChangesAsync();
        return r.Id;
    }

    protected async Task<MedicationReminder> LoadAsync(int id)
    {
        await using var db = Db();
        return await db.MedicationReminders.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    protected async Task SuspendCenterAsync()
    {
        await using var shared = Fx.Harness.NewSharedDb();
        var c = await shared.MedicalCenters.SingleAsync(x => x.Id == C.Id);
        c.CenterStatus = CenterStatus.Suspended;
        await shared.SaveChangesAsync();
    }
}

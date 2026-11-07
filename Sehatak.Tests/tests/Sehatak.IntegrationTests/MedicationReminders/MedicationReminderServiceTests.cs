using Sehatak.Application.Common;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.MedicationReminders;

[Collection(SqlServerCollection.Name)]
public class MedicationReminderServiceTests(SqlServerFixture fx) : ReminderTestBase(fx)
{
    private static readonly PagedRequest Page = new() { PageNumber = 1, PageSize = 50 };

    // ------------------------------------------------------------------ add

    [Fact]
    public async Task Add_creates_an_active_reminder_for_the_patient_starting_today()
    {
        var dto = await NewSvc().AddMedicationReminderAsync(C.Id, Pat.UserId, Req(days: ReminderDays.Monday | ReminderDays.Thursday), null);

        dto.PatientId.ShouldBe(Pat.PatientId);
        dto.IsActive.ShouldBeTrue();
        dto.Days.ShouldBe(ReminderDays.Monday | ReminderDays.Thursday);
        dto.StartDate.ShouldBe(Today);
        var row = await LoadAsync(dto.Id);
        row.MedicationName.ShouldBe("Panadol");
        row.ReminderTime.ShouldBe(new TimeOnly(8, 0));
        row.LastNotifiedDate.ShouldBeNull();
    }

    [Fact]
    public async Task IsDaily_means_every_day_whatever_days_were_sent()
    {
        var dto = await NewSvc().AddMedicationReminderAsync(C.Id, Pat.UserId, Req(daily: true, days: ReminderDays.Friday), null);
        dto.Days.ShouldBe(ReminderDays.All);
    }

    /// <summary>"3 days" must mean 3 days of reminders: today, tomorrow, the day after. Today the end date is start+3, i.e. 4 days.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_duration_of_three_days_gives_exactly_three_days_of_reminders()
    {
        var dto = await NewSvc().AddMedicationReminderAsync(C.Id, Pat.UserId, Req(duration: 3, daily: true), null);
        (dto.EndDate.DayNumber - dto.StartDate.DayNumber + 1).ShouldBe(3);
    }

    [Fact]
    public async Task A_reminder_can_be_created_for_my_sub_patient()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var dto = await NewSvc().AddMedicationReminderAsync(C.Id, Pat.UserId, Req(), sub.PatientId);
        dto.PatientId.ShouldBe(sub.PatientId);
        (await LoadAsync(dto.Id)).PatientId.ShouldBe(sub.PatientId);
    }

    [Fact]
    public async Task A_sub_patient_of_someone_else_is_refused()
    {
        var other = await C.AddPatientAsync();
        var theirs = await C.AddSubPatientAsync(other);
        await ShouldFailWith("SubPatient.NotFound", () => NewSvc().AddMedicationReminderAsync(C.Id, Pat.UserId, Req(), theirs.PatientId));
    }

    [Fact]
    public async Task Suspended_center_is_refused()
    {
        await SuspendCenterAsync();
        await ShouldFailWith("Center.NotFound", () => NewSvc().AddMedicationReminderAsync(C.Id, Pat.UserId, Req(), null));
    }

    /// <summary>An unknown/inactive patient gets "Center.NotFound": the app tells the user the clinic doesn't exist.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Inactive_patient_gets_Patient_NotFound_not_Center_NotFound()
    {
        var off = await C.AddPatientAsync(active: false);
        await ShouldFailWith("Patient.NotFound", () => NewSvc().AddMedicationReminderAsync(C.Id, off.UserId, Req(), null));
    }

    // ------------------------------------------------------------------ list

    [Fact]
    public async Task List_shows_only_my_reminders_active_first_then_by_time()
    {
        var other = await C.AddPatientAsync();
        var late = await SeedAsync(Pat.PatientId, new TimeOnly(21, 0));
        var early = await SeedAsync(Pat.PatientId, new TimeOnly(7, 0));
        var off = await SeedAsync(Pat.PatientId, new TimeOnly(6, 0), active: false);
        await SeedAsync(other.PatientId, new TimeOnly(8, 0));

        var res = await NewSvc().GetMedicationRemindersAsync(C.Id, Pat.UserId, Page, null);

        res.Items.Select(i => i.Id).ToArray().ShouldBe(new[] { early, late, off });
    }

    [Fact]
    public async Task List_of_a_sub_patient_needs_ownership_and_shows_only_his_reminders()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var mine = await SeedAsync(Pat.PatientId, new TimeOnly(8, 0));
        var his = await SeedAsync(sub.PatientId, new TimeOnly(9, 0));

        var res = await NewSvc().GetMedicationRemindersAsync(C.Id, Pat.UserId, Page, sub.PatientId);
        res.Items.Select(i => i.Id).ToArray().ShouldBe(new[] { his });
        (await NewSvc().GetMedicationRemindersAsync(C.Id, Pat.UserId, Page, null)).Items.Select(i => i.Id).ShouldNotContain(his);

        var other = await C.AddPatientAsync();
        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => NewSvc().GetMedicationRemindersAsync(C.Id, other.UserId, Page, sub.PatientId));
    }

    // ------------------------------------------------------------------ delete

    [Fact]
    public async Task Delete_removes_my_reminder()
    {
        var id = await SeedAsync(Pat.PatientId, new TimeOnly(8, 0));
        await NewSvc().DeleteMedicationReminderAsync(C.Id, Pat.UserId, id, null);
        await using var db = Db();
        db.MedicationReminders.Any(r => r.Id == id).ShouldBeFalse();
    }

    [Fact]
    public async Task Nobody_can_delete_someone_elses_reminder()
    {
        var other = await C.AddPatientAsync();
        var id = await SeedAsync(other.PatientId, new TimeOnly(8, 0));
        await ShouldFailWith("MedicationReminder.NotFound", () => NewSvc().DeleteMedicationReminderAsync(C.Id, Pat.UserId, id, null));
        (await LoadAsync(id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Parent_deletes_a_sub_patient_reminder_only_with_the_sub_patient_id()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var id = await SeedAsync(sub.PatientId, new TimeOnly(8, 0));

        await ShouldFailWith("MedicationReminder.NotFound", () => NewSvc().DeleteMedicationReminderAsync(C.Id, Pat.UserId, id, null));
        await NewSvc().DeleteMedicationReminderAsync(C.Id, Pat.UserId, id, sub.PatientId);

        await using var db = Db();
        db.MedicationReminders.Any(r => r.Id == id).ShouldBeFalse();
    }

    [Fact]
    public async Task Deleting_through_someone_elses_sub_patient_is_refused()
    {
        var other = await C.AddPatientAsync();
        var theirs = await C.AddSubPatientAsync(other);
        var id = await SeedAsync(theirs.PatientId, new TimeOnly(8, 0));
        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => NewSvc().DeleteMedicationReminderAsync(C.Id, Pat.UserId, id, theirs.PatientId));
    }
}

using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.PatientCenter;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Patients;

/// <summary>The admin's view: patients of a center and their appointments by status.</summary>
[Collection(SqlServerCollection.Name)]
public class PatientCenterServiceTests(SqlServerFixture fx) : PatientsTestBase(fx)
{
    private static GetPatientRequestDto Req(int patientId, AppointmentStatus status) => new() { patientId = patientId, status = status };

    private async Task<string> DoctorNameAsync()
    {
        var u = await UserAsync(Doc.UserId);
        return $"{u.firstName} {u.lastName}";
    }

    // ---------- one patient ----------

    [Fact]
    public async Task A_registered_patient_is_shown_with_the_name_from_the_account()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        var u = await UserAsync(Pat.UserId);

        var r = await NewCenterList().GetPatientAsync(C.Id, Req(Pat.PatientId, AppointmentStatus.Confirmed));

        r.Id.ShouldBe(Pat.PatientId);
        r.pateintName.ShouldBe($"{u.firstName} {u.lastName}");
    }

    [Fact]
    public async Task A_child_is_shown_with_its_own_name()
    {
        var kid = await C.AddSubPatientAsync(Pat);

        var r = await NewCenterList().GetPatientAsync(C.Id, Req(kid.PatientId, AppointmentStatus.Confirmed));

        r.pateintName.ShouldBe("P Test");
    }

    [Fact]
    public async Task Only_appointments_in_the_requested_status_are_returned_with_the_doctor_name()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.Confirmed);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0930, AppointmentStatus.Cancelled);

        var r = await NewCenterList().GetPatientAsync(C.Id, Req(Pat.PatientId, AppointmentStatus.Confirmed));

        var a = r.appointments.ShouldHaveSingleItem();
        a.timeSlot.ShouldBe(S0900);
        a.date.ShouldBe(Date);
        a.status.ShouldBe(AppointmentStatus.Confirmed);
        a.DoctorName.ShouldBe(await DoctorNameAsync());
    }

    [Fact]
    public async Task An_appointment_without_a_time_slot_does_not_break_the_page()
    {
        // appointments can exist without a slot (the list endpoint maps them fine); the single-patient one must too
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, slot: null);

        var r = await NewCenterList().GetPatientAsync(C.Id, Req(Pat.PatientId, AppointmentStatus.Confirmed));

        r.appointments.ShouldHaveSingleItem().timeSlot.ShouldBeNull();
    }

    [Fact]
    public async Task A_patient_without_appointments_in_that_status_has_an_empty_list()
    {
        var r = await NewCenterList().GetPatientAsync(C.Id, Req(Pat.PatientId, AppointmentStatus.Cancelled));
        r.appointments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_patient_or_unavailable_center_fail()
    {
        await ShouldFailWith("Patient.NotFound", () => NewCenterList().GetPatientAsync(C.Id, Req(999_999, AppointmentStatus.Confirmed)));
        await ShouldFailWith("Center.NotFound", () => NewCenterList().GetPatientAsync(999_999, Req(Pat.PatientId, AppointmentStatus.Confirmed)));

        var suspended = await Fx.Harness.CreateCenterAsync(CenterStatus.Suspended);
        await ShouldFailWith("Center.NotFound", () => NewCenterList().GetPatientAsync(suspended.Id, Req(1, AppointmentStatus.Confirmed)));
    }

    // ---------- list ----------

    [Fact]
    public async Task The_list_has_only_patients_with_an_appointment_in_that_status()
    {
        var other = await C.AddPatientAsync();
        var none = await C.AddPatientAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.Confirmed);
        await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S0930, AppointmentStatus.Cancelled);

        var page = await NewCenterList().GetPatientesAsync(C.Id, AppointmentStatus.Confirmed, new PagedRequest());

        page.Items.Select(i => i.Id).ShouldBe(new[] { Pat.PatientId });
        page.Items.Single().appointments.ShouldHaveSingleItem().status.ShouldBe(AppointmentStatus.Confirmed);
        page.Items.ShouldNotContain(i => i.Id == none.PatientId);
    }

    [Fact]
    public async Task The_list_includes_children_under_their_own_names()
    {
        var kid = await C.AddSubPatientAsync(Pat);
        await C.AddAppointmentAsync(Doc.DoctorId, kid.PatientId, Date, S0900);

        var page = await NewCenterList().GetPatientesAsync(C.Id, AppointmentStatus.Confirmed, new PagedRequest());

        page.Items.ShouldHaveSingleItem().pateintName.ShouldBe("P Test");
    }

    [Fact]
    public async Task The_list_is_paged()
    {
        var p2 = await C.AddPatientAsync();
        var p3 = await C.AddPatientAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await C.AddAppointmentAsync(Doc.DoctorId, p2.PatientId, Date, S0930);
        await C.AddAppointmentAsync(Doc.DoctorId, p3.PatientId, Date, S1000);

        var first = await NewCenterList().GetPatientesAsync(C.Id, AppointmentStatus.Confirmed, new PagedRequest { PageNumber = 1, PageSize = 2 });
        var second = await NewCenterList().GetPatientesAsync(C.Id, AppointmentStatus.Confirmed, new PagedRequest { PageNumber = 2, PageSize = 2 });

        first.Items.Count.ShouldBe(2);
        second.Items.Count.ShouldBe(1);
        first.TotalCount.ShouldBe(3);
        first.Items.Concat(second.Items).Select(i => i.Id).Distinct().Count().ShouldBe(3);   // no patient twice, none missing
    }

    [Fact]
    public async Task A_patient_with_two_appointments_in_the_status_is_listed_once_with_both()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0930);

        var page = await NewCenterList().GetPatientesAsync(C.Id, AppointmentStatus.Confirmed, new PagedRequest());

        page.Items.ShouldHaveSingleItem().appointments.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_list_fails_for_an_unavailable_center()
    {
        await ShouldFailWith("Center.NotFound",
            () => NewCenterList().GetPatientesAsync(999_999, AppointmentStatus.Confirmed, new PagedRequest()));
    }
}

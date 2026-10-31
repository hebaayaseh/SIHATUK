using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.MedicalRecordDto;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.MedicalRecords;

/// <summary>Reading records: the doctor's views (history of a patient, one record) and the patient's own history.</summary>
[Collection(SqlServerCollection.Name)]
public class MedicalRecordQueryTests(SqlServerFixture fx) : MedicalRecordTestBase(fx)
{
    private static PagedRequest Page(int n = 1, int size = 10) => new() { PageNumber = n, PageSize = size };

    // ------------------------------------------------------------------ GetPatientMedicalHistoryAsync (doctor)

    [Fact]
    public async Task Doctor_sees_the_patients_records_newest_first_with_bill_and_items()
    {
        var cid = await AddConsultationAsync();
        var oldRecord = await AddRecordDirectAsync(consultationId: cid, createdUtc: DateTime.UtcNow.AddDays(-10), diagnosis: "old");
        await Add(AppointmentReq(Appt, items: [It(Bandage, 2)]));                                  // 60
        var recent = await RecordIdForAppointmentAsync(Appt);
        await using (var db = Db())
        {
            var r = await db.MedicalRecords.SingleAsync(x => x.Id == recent);
            r.CreatedAt = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        var res = await NewRecordService().GetPatientMedicalHistoryAsync(C.Id, Doc.UserId, Pat.PatientId, Page());

        res.TotalCount.ShouldBe(2);
        res.Items.Select(i => i.Id).ToArray().ShouldBe(new[] { recent, oldRecord });

        var withBill = res.Items[0];
        withBill.AppointmentId.ShouldBe(Appt);
        withBill.ConsultationCost.ShouldBe(50m);
        withBill.BillAmount.ShouldBe(60m);
        withBill.Items!.Count.ShouldBe(1);
        withBill.Items![0].Quantity.ShouldBe(2);
        withBill.Items![0].ServiceName.ShouldBe("Bandage");
        withBill.PatientName.ShouldNotBeNullOrWhiteSpace();
        withBill.DoctorName.ShouldNotBeNullOrWhiteSpace();

        var consultation = res.Items[1];
        consultation.ConsultationId.ShouldBe(cid);
        consultation.BillAmount.ShouldBeNull();
        consultation.Items.ShouldBeNull();
        consultation.Diagnosis.ShouldBe("old");
    }

    [Fact]
    public async Task Doctor_history_contains_only_that_patient()
    {
        var other = await C.AddPatientAsync();
        var a2 = await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S0930);
        await AddRecordDirectAsync(patientId: other.PatientId, appointmentId: a2);
        var mine = await AddRecordDirectAsync(appointmentId: Appt);

        var res = await NewRecordService().GetPatientMedicalHistoryAsync(C.Id, Doc.UserId, Pat.PatientId, Page());

        res.Items.Single().Id.ShouldBe(mine);
    }

    [Fact]
    public async Task Doctor_history_pages_results()
    {
        for (var i = 0; i < 5; i++) await AddRecordDirectAsync(consultationId: await AddConsultationAsync());
        var res = await NewRecordService().GetPatientMedicalHistoryAsync(C.Id, Doc.UserId, Pat.PatientId, Page(3, 2));
        res.TotalCount.ShouldBe(5);
        res.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Doctor_history_is_empty_for_a_patient_without_records()
    {
        var res = await NewRecordService().GetPatientMedicalHistoryAsync(C.Id, Doc.UserId, Pat.PatientId, Page());
        res.TotalCount.ShouldBe(0);
        res.Items.ShouldBeEmpty();
    }

    [Fact]
    public Task History_Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => NewRecordService().GetPatientMedicalHistoryAsync(C.Id, Doc.UserId, 999_999, Page()));

    [Fact]
    public async Task History_Patient_NotFound_for_an_inactive_patient()
    {
        var gone = await C.AddPatientAsync(active: false);
        await ShouldFailWith("Patient.NotFound", () => NewRecordService().GetPatientMedicalHistoryAsync(C.Id, Doc.UserId, gone.PatientId, Page()));
    }

    [Fact]
    public Task History_Doctor_NotFound_when_the_caller_is_not_a_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => NewRecordService().GetPatientMedicalHistoryAsync(C.Id, Rec, Pat.PatientId, Page()));

    [Fact]
    public Task History_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewRecordService().GetPatientMedicalHistoryAsync(999_999, Doc.UserId, Pat.PatientId, Page()));

    // ------------------------------------------------------------------ GetMedicalRecordByIdAsync (doctor)

    [Fact]
    public async Task Doctor_opens_one_appointment_record_with_its_bill()
    {
        await Add(AppointmentReq(items: [It(Bandage, 2), It(Injection, 1)]));
        var id = await RecordIdForAppointmentAsync(Appt);
        var items = await AppointmentItemIdsAsync(Appt);

        var r = await NewRecordService().GetMedicalRecordByIdAsync(C.Id, Doc.UserId, id);

        r.Id.ShouldBe(id);
        r.PatientId.ShouldBe(Pat.PatientId);
        r.DoctorId.ShouldBe(Doc.DoctorId);
        r.AppointmentId.ShouldBe(Appt);
        r.ConsultationId.ShouldBeNull();
        r.Diagnosis.ShouldBe("flu");
        r.Prescription.ShouldBe("rest");
        r.Notes.ShouldBe("n0");
        r.ConsultationCost.ShouldBe(50m);
        r.BillAmount.ShouldBe(72m);
        r.PatientName.ShouldNotBeNullOrWhiteSpace();
        r.DoctorName.ShouldNotBeNullOrWhiteSpace();
        r.Items!.Select(i => i.Id).ShouldBe(items, ignoreOrder: true);      // here ids are the appointment item ids
        r.Items!.Sum(i => i.TotalPrice).ShouldBe(22m);
    }

    [Fact]
    public async Task Doctor_opens_a_consultation_record()
    {
        var cid = await AddConsultationAsync();
        await Add(ConsultationReq(cid));
        var id = await RecordIdForConsultationAsync(cid);

        var r = await NewRecordService().GetMedicalRecordByIdAsync(C.Id, Doc.UserId, id);

        r.ConsultationId.ShouldBe(cid);
        r.AppointmentId.ShouldBeNull();
        r.BillAmount.ShouldBeNull();
        r.Items.ShouldBeNull();
        r.Diagnosis.ShouldBe("cough");
    }

    [Fact]
    public Task Open_MedicalRecord_NotFound() =>
        ShouldFailWith("MedicalRecord.NotFound", () => NewRecordService().GetMedicalRecordByIdAsync(C.Id, Doc.UserId, 999_999));

    [Fact]
    public Task Open_Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => NewRecordService().GetMedicalRecordByIdAsync(C.Id, Pat.UserId, 1));

    [Fact]
    public Task Open_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewRecordService().GetMedicalRecordByIdAsync(999_999, Doc.UserId, 1));

    // ------------------------------------------------------------------ PatientgetMedicalRecordHistoryAsync (patient)

    [Fact]
    public async Task Patient_sees_only_his_own_records_newest_first()
    {
        var older = await AddRecordDirectAsync(consultationId: await AddConsultationAsync(), createdUtc: DateTime.UtcNow.AddDays(-5), diagnosis: "older");
        var newer = await AddRecordDirectAsync(consultationId: await AddConsultationAsync(), createdUtc: DateTime.UtcNow.AddDays(-1), diagnosis: "newer");
        var other = await C.AddPatientAsync();
        await AddRecordDirectAsync(patientId: other.PatientId, consultationId: await AddConsultationAsync(patientId: other.PatientId));
        var sub = await C.AddSubPatientAsync(Pat);
        await AddRecordDirectAsync(patientId: sub.PatientId, consultationId: await AddConsultationAsync(patientId: sub.PatientId));

        var r = await NewRecordService().PatientgetMedicalRecordHistoryAsync(C.Id, Pat.UserId, Page(), null);

        r.Items.Select(i => i.medicalRecordId).ToArray().ShouldBe(new[] { newer, older });
        r.Items[0].Diagnosis.ShouldBe("newer");
        r.Items[0].DoctorId.ShouldBe(Doc.DoctorId);
        r.Items[0].DoctorName.ShouldNotBeNullOrWhiteSpace();
        r.Items[0].Prescription.ShouldBe("p");
    }

    [Fact]
    public async Task Patient_sees_the_childs_records_when_a_child_is_selected()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        await AddRecordDirectAsync(consultationId: await AddConsultationAsync());
        var kid = await AddRecordDirectAsync(patientId: sub.PatientId, consultationId: await AddConsultationAsync(patientId: sub.PatientId));

        var r = await NewRecordService().PatientgetMedicalRecordHistoryAsync(C.Id, Pat.UserId, Page(), sub.PatientId);

        r.Items.Single().medicalRecordId.ShouldBe(kid);
    }

    [Fact]
    public async Task Patient_history_pages_results()
    {
        for (var i = 0; i < 4; i++) await AddRecordDirectAsync(consultationId: await AddConsultationAsync());
        var r = await NewRecordService().PatientgetMedicalRecordHistoryAsync(C.Id, Pat.UserId, Page(2, 3), null);
        r.TotalCount.ShouldBe(4);
        r.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Patient_history_SubPatient_NotFound_for_someone_elses_child()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);
        await ShouldFailWith("SubPatient.NotFound", () => NewRecordService().PatientgetMedicalRecordHistoryAsync(C.Id, Pat.UserId, Page(), child.PatientId));
    }

    [Fact]
    public Task Patient_history_Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => NewRecordService().PatientgetMedicalRecordHistoryAsync(C.Id, 999_999, Page(), null));

    [Fact]
    public async Task Patient_history_Patient_NotFound_for_an_inactive_account()
    {
        var gone = await C.AddPatientAsync(active: false);
        await ShouldFailWith("Patient.NotFound", () => NewRecordService().PatientgetMedicalRecordHistoryAsync(C.Id, gone.UserId, Page(), null));
    }

    [Fact]
    public Task Patient_history_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewRecordService().PatientgetMedicalRecordHistoryAsync(999_999, Pat.UserId, Page(), null));

    // ------------------------------------------------------------------ whole journey

    [Fact]
    public async Task Full_journey_add_edit_then_both_sides_read_the_same_record()
    {
        await Add(AppointmentReq(items: [It(Injection, 1)]));
        var id = await RecordIdForAppointmentAsync(Appt);
        await Edit(new UpdateMedicalRecordRequestDto { PatientId = Pat.PatientId, MedicalRecordId = id, AppointmentId = Appt, Diagnosis = "final", CustomConsultationPrice = 40m });

        var doctor = await NewRecordService().GetMedicalRecordByIdAsync(C.Id, Doc.UserId, id);
        var patient = await NewRecordService().PatientgetMedicalRecordHistoryAsync(C.Id, Pat.UserId, Page(), null);

        doctor.Diagnosis.ShouldBe("final");
        doctor.BillAmount.ShouldBe(52m);              // 40 + 12
        patient.Items.Single().Diagnosis.ShouldBe("final");
        (await PaymentOfAsync(Appt)).Amount.ShouldBe(52m);
    }
}

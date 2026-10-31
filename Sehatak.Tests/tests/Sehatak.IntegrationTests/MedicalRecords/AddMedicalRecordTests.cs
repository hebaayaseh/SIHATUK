using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.MedicalRecordDto;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.MedicalRecords;

/// <summary>The doctor writes the record: for an appointment (also creates the bill) or for an online consultation.</summary>
[Collection(SqlServerCollection.Name)]
public class AddMedicalRecordTests(SqlServerFixture fx) : MedicalRecordTestBase(fx)
{
    // ------------------------------------------------------------------ appointment record

    [Fact]
    public async Task Appointment_record_is_saved_and_a_Pending_payment_for_the_visit_price_is_created()
    {
        var r = await Add(AppointmentReq());

        r.BillAmount.ShouldBe(50m);
        r.PatientId.ShouldBe(Pat.PatientId);
        r.PatientName.ShouldNotBeNullOrWhiteSpace();
        r.DoctorName.ShouldNotBeNullOrWhiteSpace();
        r.AppointmentId.ShouldBe(Appt);
        r.Diagnosis.ShouldBe("flu");
        r.Items.ShouldBeNull();
        ShouldBeNowUtc(r.CreatedAt);

        var rec = await LoadRecordAsync(await RecordIdForAppointmentAsync(Appt));
        rec.DoctorId.ShouldBe(Doc.DoctorId);
        rec.PatientId.ShouldBe(Pat.PatientId);
        rec.Diagnosis.ShouldBe("flu");
        rec.Prescription.ShouldBe("rest");
        rec.Notes.ShouldBe("n0");
        rec.ConsultationId.ShouldBeNull();
        ShouldBeNowUtc(rec.CreatedAt);
        ShouldBeNowUtc(rec.UpdatedAt);

        var appt = await LoadAppointmentAsync(Appt);
        appt.ConsultationCost.ShouldBe(50m);
        appt.BillAmount.ShouldBe(50m);

        var pay = await PaymentOfAsync(Appt);
        pay.Amount.ShouldBe(50m);
        pay.Type.ShouldBe(PaymentType.Appointment);
        pay.Status.ShouldBe(PaymentStatus.Pending);
        pay.PatientId.ShouldBe(Pat.PatientId);

        (await CountAuditAsync("AddMedicalRecord")).ShouldBe(1);
    }

    [Fact]
    public async Task Extra_items_are_priced_per_quantity_and_added_to_the_bill()
    {
        var r = await Add(AppointmentReq(items: [It(Bandage, 2), It(Injection, 1)]));   // 50 + 2*5 + 12

        r.BillAmount.ShouldBe(72m);
        r.Items!.Count.ShouldBe(2);
        r.Items!.Single(i => i.ServiceName == "Bandage").TotalPrice.ShouldBe(10m);

        await using var db = Db();
        var items = await db.AppointmentItems.Where(i => i.AppointmentId == Appt).ToListAsync();
        items.Count.ShouldBe(2);
        var b = items.Single(i => i.ServicePriceId == Bandage);
        b.Quantity.ShouldBe(2);
        b.UnitPrice.ShouldBe(5m);
        b.TotalPrice.ShouldBe(10m);
        (await db.Payments.SingleAsync(p => p.AppointmentId == Appt)).Amount.ShouldBe(72m);
        (await db.Appointments.SingleAsync(a => a.Id == Appt)).BillAmount.ShouldBe(72m);
    }

    [Fact]
    public async Task A_custom_visit_price_replaces_the_price_list()
    {
        var r = await Add(AppointmentReq(cost: 80m));
        r.BillAmount.ShouldBe(80m);
        (await LoadAppointmentAsync(Appt)).ConsultationCost.ShouldBe(80m);
        (await PaymentOfAsync(Appt)).Amount.ShouldBe(80m);
    }

    [Fact]
    public async Task A_free_visit_is_allowed()
    {
        var r = await Add(AppointmentReq(cost: 0m, items: [It(Injection, 1)]));
        r.BillAmount.ShouldBe(12m);
        (await LoadAppointmentAsync(Appt)).ConsultationCost.ShouldBe(0m);
    }

    [Fact]
    public async Task A_negative_custom_price_is_ignored_and_the_price_list_is_used()
    {
        var r = await Add(AppointmentReq(cost: -5m));
        r.BillAmount.ShouldBe(50m);
    }

    [Fact]
    public async Task A_follow_up_visit_uses_the_follow_up_price()
    {
        await using (var db = Db()) { (await db.Appointments.SingleAsync(a => a.Id == Appt)).IsFollowUp = true; await db.SaveChangesAsync(); }
        var r = await Add(AppointmentReq());
        r.BillAmount.ShouldBe(30m);
    }

    [Fact]
    public async Task A_custom_price_also_wins_for_a_follow_up()
    {
        await using (var db = Db()) { (await db.Appointments.SingleAsync(a => a.Id == Appt)).IsFollowUp = true; await db.SaveChangesAsync(); }
        (await Add(AppointmentReq(cost: 10m))).BillAmount.ShouldBe(10m);
    }

    [Fact]
    public async Task Record_for_a_child_patient()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var appt = await C.AddAppointmentAsync(Doc.DoctorId, sub.PatientId, Date, S0930);

        var r = await Add(AppointmentReq(appt, sub.PatientId));

        r.PatientId.ShouldBe(sub.PatientId);
        r.PatientName.ShouldNotBeNullOrWhiteSpace();
        (await PaymentOfAsync(appt)).PatientId.ShouldBe(sub.PatientId);
    }

    [Fact]
    public async Task ServicePrice_NotFound_when_there_is_no_active_visit_price_and_nothing_is_saved()
    {
        await using (var db = Db())
        {
            foreach (var s in await db.ServicePrices.Where(s => s.Type == ServiceType.Appointment).ToListAsync()) s.IsActive = false;
            await db.SaveChangesAsync();
        }

        await ShouldFailWith("ServicePrice.NotFound", () => Add(AppointmentReq()));

        await using var db2 = Db();
        (await db2.MedicalRecords.CountAsync()).ShouldBe(0);
        (await db2.Payments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ServicePrice_NotFound_when_there_is_no_active_follow_up_price()
    {
        await using (var db = Db())
        {
            (await db.Appointments.SingleAsync(a => a.Id == Appt)).IsFollowUp = true;
            foreach (var s in await db.ServicePrices.Where(s => s.Type == ServiceType.FollowUp).ToListAsync()) s.IsActive = false;
            await db.SaveChangesAsync();
        }
        await ShouldFailWith("ServicePrice.NotFound", () => Add(AppointmentReq()));
    }

    [Fact]
    public async Task ServicePrice_NotFound_for_an_unknown_inactive_or_wrong_type_item_and_nothing_is_saved()
    {
        var lab = await AddServiceAsync("CBC", 20m);   // LabTest, not an extra
        await using (var db = Db()) { (await db.ServicePrices.SingleAsync(s => s.Id == Injection)).IsActive = false; await db.SaveChangesAsync(); }

        await ShouldFailWith("ServicePrice.NotFound", () => Add(AppointmentReq(items: [It(999_999, 1)])));
        await ShouldFailWith("ServicePrice.NotFound", () => Add(AppointmentReq(items: [It(lab, 1)])));
        await ShouldFailWith("ServicePrice.NotFound", () => Add(AppointmentReq(items: [It(Bandage, 1), It(Injection, 1)])));

        await using var db2 = Db();
        (await db2.MedicalRecords.CountAsync()).ShouldBe(0);
        (await db2.AppointmentItems.CountAsync()).ShouldBe(0);
        (await db2.Payments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Appointment_NotFound_for_a_cancelled_appointment()
    {
        var a = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S1000, AppointmentStatus.Cancelled);
        await ShouldFailWith("Appointment.NotFound", () => Add(AppointmentReq(a)));
    }

    [Fact]
    public async Task Appointment_NotFound_for_another_doctors_appointment()
    {
        var doc2 = await C.AddDoctorAsync();
        await ShouldFailWith("Appointment.NotFound", () => Add(AppointmentReq(), doc2.UserId));
    }

    [Fact]
    public async Task Appointment_NotFound_when_it_belongs_to_another_patient()
    {
        var other = await C.AddPatientAsync();
        await ShouldFailWith("Appointment.NotFound", () => Add(AppointmentReq(patientId: other.PatientId)));
    }

    [Fact]
    public Task Appointment_NotFound_for_an_unknown_appointment() =>
        ShouldFailWith("Appointment.NotFound", () => Add(AppointmentReq(999_999)));

    // ------------------------------------------------------------------ known bugs (appointment record)

    /// <summary>The response never fills Id, so the app cannot open or edit the record it just created (Id = 0).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_response_contains_the_new_record_id()
    {
        var r = await Add(AppointmentReq());
        r.Id.ShouldBe(await RecordIdForAppointmentAsync(Appt));
    }

    /// <summary>The response puts the USER id in DoctorId; every other endpoint returns the doctor id.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_response_returns_the_doctor_id_not_the_user_id()
    {
        var r = await Add(AppointmentReq());
        r.DoctorId.ShouldBe(Doc.DoctorId);
    }

    /// <summary>Add returns the ServicePrice id in "Id" (and no ServicePriceId); Edit's RemoveItemIds and the Get endpoints use the AppointmentItem id.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Item_ids_in_the_response_can_be_used_to_remove_the_item_later()
    {
        var r = await Add(AppointmentReq(items: [It(Bandage, 1)]));
        var saved = await AppointmentItemIdsAsync(Appt);

        r.Items!.Single().Id.ShouldBe(saved.Single());
        r.Items!.Single().ServicePriceId.ShouldBe(Bandage);
    }

    /// <summary>The audit row uses EntityType "MedicalRecord" but stores the appointment (or consultation) id as EntityId.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_audit_row_points_at_the_medical_record()
    {
        // two throw-away records first, so the new record's id cannot equal the appointment id by accident
        var c1 = await AddConsultationAsync(); await AddRecordDirectAsync(consultationId: c1);
        var c2 = await AddConsultationAsync(); await AddRecordDirectAsync(consultationId: c2);

        await Add(AppointmentReq());

        var recordId = await RecordIdForAppointmentAsync(Appt);
        await using var db = Db();
        (await db.AuditLogs.SingleAsync(a => a.Action == "AddMedicalRecord")).EntityId.ShouldBe(recordId);
    }

    /// <summary>MedicalRecord.AppointmentId is unique, so a second record for the same appointment throws a raw DbUpdateException (HTTP 500).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_second_record_for_the_same_appointment_is_refused_with_a_business_error()
    {
        await Add(AppointmentReq());
        await ShouldFailWith("MedicalRecord.AlreadyExists", () => Add(AppointmentReq()));
        await using var db = Db();
        (await db.MedicalRecords.CountAsync()).ShouldBe(1);
    }

   
    // ------------------------------------------------------------------ consultation record

    [Theory]
    [InlineData(ConsultationStatus.Accepted)]
    [InlineData(ConsultationStatus.Completed)]
    public async Task Consultation_record_is_saved_without_a_payment(ConsultationStatus status)
    {
        var cid = await AddConsultationAsync(status);

        var r = await Add(ConsultationReq(cid));

        r.ConsultationId.ShouldBe(cid);
        r.AppointmentId.ShouldBeNull();
        r.BillAmount.ShouldBe(60m);

        var rec = await LoadRecordAsync(await RecordIdForConsultationAsync(cid));
        rec.AppointmentId.ShouldBeNull();
        rec.Diagnosis.ShouldBe("cough");
        rec.DoctorId.ShouldBe(Doc.DoctorId);

        await using var db = Db();
        (await db.Payments.CountAsync()).ShouldBe(0);
        (await db.AppointmentItems.CountAsync()).ShouldBe(0);
        (await CountAuditAsync("AddMedicalRecord")).ShouldBe(1);
    }

    /// <summary>Only a Rejected consultation is refused today, so a Pending (not paid / not confirmed) or Cancelled one can get a medical record. Confirm the rule with the team.</summary>
    [Theory]
    [Trait("Category", "KnownBug")]
    [InlineData(ConsultationStatus.Pending)]
    [InlineData(ConsultationStatus.Cancelled)]
    public async Task A_consultation_that_never_took_place_cannot_get_a_record(ConsultationStatus status)
    {
        var cid = await AddConsultationAsync(status);
        await ShouldFailWith("Consultation.NotFound", () => Add(ConsultationReq(cid)));
    }

    [Fact]
    public async Task Consultation_NotFound_for_a_Rejected_consultation()
    {
        var cid = await AddConsultationAsync(ConsultationStatus.Rejected);
        await ShouldFailWith("Consultation.NotFound", () => Add(ConsultationReq(cid)));
    }

    [Fact]
    public async Task Consultation_NotFound_for_another_doctor_or_another_patient()
    {
        var doc2 = await C.AddDoctorAsync();
        var other = await C.AddPatientAsync();
        var theirs = await AddConsultationAsync(doctorId: doc2.DoctorId);
        var othersPatient = await AddConsultationAsync(patientId: other.PatientId);

        await ShouldFailWith("Consultation.NotFound", () => Add(ConsultationReq(theirs)));
        await ShouldFailWith("Consultation.NotFound", () => Add(ConsultationReq(othersPatient)));
    }

    [Fact]
    public Task Consultation_NotFound_for_an_unknown_consultation() =>
        ShouldFailWith("Consultation.NotFound", () => Add(ConsultationReq(999_999)));

    [Fact]
    public async Task ServicePrice_NotFound_when_there_is_no_active_consultation_price_and_nothing_is_saved()
    {
        var cid = await AddConsultationAsync();
        await using (var db = Db())
        {
            foreach (var s in await db.ServicePrices.Where(s => s.Type == ServiceType.ConsultationCost).ToListAsync()) s.IsActive = false;
            await db.SaveChangesAsync();
        }

        await ShouldFailWith("ServicePrice.NotFound", () => Add(ConsultationReq(cid)));

        await using var db2 = Db();
        (await db2.MedicalRecords.CountAsync()).ShouldBe(0);
    }

    // ------------------------------------------------------------------ common validation

    [Fact]
    public Task MustLinkToAppointmentOrConsultation_when_neither_is_given() =>
        ShouldFailWith("MedicalRecord.MustLinkToAppointmentOrConsultation",
            () => Add(new MedicalRecordDetailRequestDto { PatientId = Pat.PatientId }));

    [Fact]
    public async Task CannotLinkToBoth()
    {
        var cid = await AddConsultationAsync();
        await ShouldFailWith("MedicalRecord.CannotLinkToBoth",
            () => Add(new MedicalRecordDetailRequestDto { PatientId = Pat.PatientId, AppointmentId = Appt, ConsultationId = cid }));
    }

    [Fact]
    public Task Patient_NotFound_for_an_unknown_patient() =>
        ShouldFailWith("Patient.NotFound", () => Add(AppointmentReq(patientId: 999_999)));

    [Fact]
    public async Task Patient_NotFound_for_an_inactive_patient()
    {
        var gone = await C.AddPatientAsync(active: false);
        await ShouldFailWith("Patient.NotFound", () => Add(AppointmentReq(patientId: gone.PatientId)));
    }

    [Fact]
    public Task Doctor_NotFound_when_the_caller_is_not_a_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => Add(AppointmentReq(), Rec));

    [Fact]
    public async Task Doctor_NotFound_for_an_inactive_doctor()
    {
        var inactive = await C.AddDoctorAsync(active: false);
        await ShouldFailWith("Doctor.NotFound", () => Add(AppointmentReq(), inactive.UserId));
    }

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewRecordService().AddMedicalRecordAsync(999_999, Doc.UserId, AppointmentReq()));
}

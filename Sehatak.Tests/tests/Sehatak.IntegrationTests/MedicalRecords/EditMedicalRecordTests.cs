using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.MedicalRecordDto;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.MedicalRecords;

/// <summary>Editing a record: text fields, and for appointment records the bill (items, visit price, payment amount).</summary>
[Collection(SqlServerCollection.Name)]
public class EditMedicalRecordTests(SqlServerFixture fx) : MedicalRecordTestBase(fx)
{
    private int _record;                    // the record created in SetUp for Appt

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        // record + bill through the real service: 50 + 2*Bandage(5) + 1*Injection(12) = 72
        await Add(AppointmentReq(items: [It(Bandage, 2), It(Injection, 1)]));
        _record = await RecordIdForAppointmentAsync(Appt);
    }

    private UpdateMedicalRecordRequestDto Req(int? appointmentId = null, int? recordId = null, int? patientId = null) => new()
    {
        PatientId = patientId ?? Pat.PatientId,
        MedicalRecordId = recordId ?? _record,
        AppointmentId = appointmentId ?? Appt
    };

    // ------------------------------------------------------------------ appointment record

    [Fact]
    public async Task Only_the_fields_that_are_sent_change()
    {
        var req = Req(); req.Prescription = "p1";
        var r = await Edit(req);

        r.Prescription.ShouldBe("p1");
        var rec = await LoadRecordAsync(_record);
        rec.Prescription.ShouldBe("p1");
        rec.Diagnosis.ShouldBe("flu");      // untouched
        rec.Notes.ShouldBe("n0");           // untouched
    }

    [Fact]
    public async Task Diagnosis_and_notes_can_be_changed()
    {
        var req = Req(); req.Diagnosis = "cold"; req.RecordNotes = "rest 3 days";
        await Edit(req);

        var rec = await LoadRecordAsync(_record);
        rec.Diagnosis.ShouldBe("cold");
        rec.Notes.ShouldBe("rest 3 days");
        rec.Prescription.ShouldBe("rest");
    }

    [Fact]
    public async Task Text_only_edit_does_not_touch_the_bill()
    {
        var req = Req(); req.Diagnosis = "x";
        var r = await Edit(req);

        r.BillAmount.ShouldBe(72m);
        (await PaymentOfAsync(Appt)).Amount.ShouldBe(72m);
        (await LoadAppointmentAsync(Appt)).BillAmount.ShouldBe(72m);
    }

    [Fact]
    public async Task Adding_an_item_raises_the_bill_and_the_payment()
    {
        var req = Req(); req.Items = [It(Bandage, 3)];       // +15
        var r = await Edit(req);

        r.BillAmount.ShouldBe(87m);
        (await PaymentOfAsync(Appt)).Amount.ShouldBe(87m);
        (await LoadAppointmentAsync(Appt)).BillAmount.ShouldBe(87m);
        (await AppointmentItemIdsAsync(Appt)).Length.ShouldBe(3);
    }

    [Fact]
    public async Task Removing_an_item_lowers_the_bill_by_its_total()
    {
        var items = await AppointmentItemIdsAsync(Appt);       // [Bandage x2 = 10, Injection x1 = 12]
        var req = Req(); req.RemoveItemIds = [items[0]];
        var r = await Edit(req);

        r.BillAmount.ShouldBe(62m);
        (await PaymentOfAsync(Appt)).Amount.ShouldBe(62m);
        (await AppointmentItemIdsAsync(Appt)).ShouldBe(new[] { items[1] });
    }

    [Fact]
    public async Task Unknown_or_foreign_item_ids_are_ignored_when_removing()
    {
        var appt2 = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0930);
        await Add(AppointmentReq(appt2, items: [It(Injection, 1)]));
        var foreign = (await AppointmentItemIdsAsync(appt2)).Single();

        var req = Req(); req.RemoveItemIds = [999_999, foreign];
        var r = await Edit(req);

        r.BillAmount.ShouldBe(72m);
        (await AppointmentItemIdsAsync(appt2)).Length.ShouldBe(1);      // the other appointment keeps its item
        (await AppointmentItemIdsAsync(Appt)).Length.ShouldBe(2);
    }

    [Fact]
    public async Task A_new_visit_price_replaces_the_old_one_in_the_bill()
    {
        var req = Req(); req.CustomConsultationPrice = 80m;       // 72 - 50 + 80
        var r = await Edit(req);

        r.BillAmount.ShouldBe(102m);
        (await LoadAppointmentAsync(Appt)).ConsultationCost.ShouldBe(80m);
        (await PaymentOfAsync(Appt)).Amount.ShouldBe(102m);
    }

    [Fact]
    public async Task Visit_price_can_be_set_to_zero()
    {
        var req = Req(); req.CustomConsultationPrice = 0m;        // 72 - 50
        (await Edit(req)).BillAmount.ShouldBe(22m);
    }

    [Fact]
    public async Task Remove_add_and_price_change_in_one_call()
    {
        var items = await AppointmentItemIdsAsync(Appt);
        var req = Req();
        req.RemoveItemIds = [items[1]];                // -12  -> 60
        req.Items = [It(Bandage, 1)];                  // +5   -> 65
        req.CustomConsultationPrice = 40m;             // -50 +40 -> 55
        var r = await Edit(req);

        r.BillAmount.ShouldBe(55m);
        (await PaymentOfAsync(Appt)).Amount.ShouldBe(55m);
    }

    [Fact]
    public async Task Payment_notes_are_saved_on_the_payment()
    {
        var req = Req(); req.PaymentNotes = "discount agreed";
        await Edit(req);
        (await PaymentOfAsync(Appt)).Notes.ShouldBe("discount agreed");
    }

    [Fact]
    public async Task Edit_is_written_to_the_audit_log_with_the_record_id()
    {
        var req = Req(); req.Diagnosis = "x";
        await Edit(req);

        await using var db = Db();
        (await db.AuditLogs.SingleAsync(a => a.Action == "EditMedicalRecord")).EntityId.ShouldBe(_record);
    }

    [Fact]
    public async Task ServicePrice_NotFound_for_a_bad_item_and_nothing_is_saved()
    {
        var lab = await AddServiceAsync("CBC", 20m);
        var req = Req(); req.Diagnosis = "changed"; req.Items = [It(Bandage, 1), It(lab, 1)];

        await ShouldFailWith("ServicePrice.NotFound", () => Edit(req));

        (await LoadRecordAsync(_record)).Diagnosis.ShouldBe("flu");
        (await PaymentOfAsync(Appt)).Amount.ShouldBe(72m);
        (await AppointmentItemIdsAsync(Appt)).Length.ShouldBe(2);
    }

    [Fact]
    public async Task Payment_NotFound_when_the_appointment_has_no_payment()
    {
        var appt2 = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0930);
        await using (var db = Db()) { (await db.Appointments.SingleAsync(a => a.Id == appt2)).BillAmount = 50m; await db.SaveChangesAsync(); }
        var rec2 = await AddRecordDirectAsync(appointmentId: appt2);

        await ShouldFailWith("Payment.NotFound", () => Edit(Req(appt2, rec2)));
    }

    [Fact]
    public async Task Appointment_NotFound_for_a_cancelled_appointment()
    {
        await using (var db = Db()) { (await db.Appointments.SingleAsync(a => a.Id == Appt)).appointmentStatus = AppointmentStatus.Cancelled; await db.SaveChangesAsync(); }
        await ShouldFailWith("Appointment.NotFound", () => Edit(Req()));
    }

    [Fact]
    public async Task Record_NotFound_for_another_doctor()
    {
        var doc2 = await C.AddDoctorAsync();
        await ShouldFailWith("Medical.NotFound", () => Edit(Req(), doc2.UserId));
    }

    [Fact]
    public Task Record_NotFound_for_an_unknown_record() =>
        ShouldFailWith("Medical.NotFound", () => Edit(Req(recordId: 999_999)));

    [Fact]
    public Task Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => Edit(Req(patientId: 999_999)));

    [Fact]
    public Task Doctor_NotFound_when_the_caller_is_not_a_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => Edit(Req(), Rec));

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewRecordService().EditMedicalRecordAsync(999_999, Doc.UserId, Req()));

    [Fact]
    public Task MustLinkToAppointmentOrConsultation() =>
        ShouldFailWith("MedicalRecord.MustLinkToAppointmentOrConsultation",
            () => Edit(new UpdateMedicalRecordRequestDto { PatientId = Pat.PatientId, MedicalRecordId = _record }));

    [Fact]
    public async Task CannotLinkToBoth()
    {
        var cid = await AddConsultationAsync();
        var req = Req(); req.ConsultationId = cid;
        await ShouldFailWith("MedicalRecord.CannotLinkToBoth", () => Edit(req));
    }

    [Fact]
    public async Task Text_can_still_be_edited_after_the_payment_is_paid()
    {
        await using (var db = Db()) { (await db.Payments.SingleAsync(p => p.AppointmentId == Appt)).Status = PaymentStatus.Paid; await db.SaveChangesAsync(); }

        var req = Req(); req.Diagnosis = "updated after payment"; req.RecordNotes = "n1";
        await Edit(req);

        (await LoadRecordAsync(_record)).Diagnosis.ShouldBe("updated after payment");
        (await PaymentOfAsync(Appt)).Amount.ShouldBe(72m);
        (await PaymentOfAsync(Appt)).Status.ShouldBe(PaymentStatus.Paid);
    }

    // ------------------------------------------------------------------ known bugs

    /// <summary>
    /// The record is looked up by id only. The doctor can pass ANY record id together with one of his own appointments:
    /// the text of the other record is overwritten and the bill of his appointment is changed.
    /// Fix: load the record with `m.Id == id &amp;&amp; m.DoctorId == doctor.Id &amp;&amp; m.PatientId == request.PatientId
    /// &amp;&amp; m.AppointmentId == request.AppointmentId` (and the same with ConsultationId).
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_record_cannot_be_edited_through_a_different_appointment_of_the_same_doctor()
    {
        var appt2 = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0930);
        await Add(AppointmentReq(appt2));

        var req = Req(appointmentId: appt2);        // record of Appt, appointment appt2
        req.Diagnosis = "overwritten";

        await ShouldFailWith("Medical.NotFound", () => Edit(req));
        (await LoadRecordAsync(_record)).Diagnosis.ShouldBe("flu");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_doctor_cannot_edit_another_doctors_record()
    {
        var doc2 = await C.AddDoctorAsync();
        var appt2 = await C.AddAppointmentAsync(doc2.DoctorId, Pat.PatientId, Date, S1000);
        await Add(AppointmentReq(appt2), doc2.UserId);

        var req = Req(appointmentId: appt2);        // doc2's own appointment, but doc1's record
        req.Diagnosis = "overwritten";

        await ShouldFailWith("Medical.NotFound", () => Edit(req, doc2.UserId));
        (await LoadRecordAsync(_record)).Diagnosis.ShouldBe("flu");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_consultation_record_cannot_be_edited_through_another_doctors_consultation()
    {
        var doc2 = await C.AddDoctorAsync();
        var c1 = await AddConsultationAsync();
        var c2 = await AddConsultationAsync(doctorId: doc2.DoctorId);
        await Add(ConsultationReq(c1));
        await Add(ConsultationReq(c2), doc2.UserId);
        var rec1 = await RecordIdForConsultationAsync(c1);

        var req = new UpdateMedicalRecordRequestDto { PatientId = Pat.PatientId, MedicalRecordId = rec1, ConsultationId = c2, Diagnosis = "overwritten" };

        await ShouldFailWith("Medical.NotFound", () => Edit(req, doc2.UserId));
        (await LoadRecordAsync(rec1)).Diagnosis.ShouldBe("cough");
    }

    

    /// <summary>The response says UpdateAt = now but the row keeps the old UpdatedAt.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Editing_moves_UpdatedAt()
    {
        await using (var db = Db()) { (await db.MedicalRecords.SingleAsync(r => r.Id == _record)).UpdatedAt = DateTime.UtcNow.AddHours(-3); await db.SaveChangesAsync(); }

        var req = Req(); req.Diagnosis = "x";
        await Edit(req);

        ShouldBeNowUtc((await LoadRecordAsync(_record)).UpdatedAt);
    }

    /// <summary>The response echoes what the request sent (null) instead of the saved diagnosis.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_response_shows_the_saved_diagnosis_even_when_it_was_not_sent()
    {
        var req = Req(); req.RecordNotes = "only notes";
        (await Edit(req)).Diagnosis.ShouldBe("flu");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_response_returns_the_doctor_id_not_the_user_id()
    {
        var req = Req(); req.Diagnosis = "x";
        (await Edit(req)).DoctorId.ShouldBe(Doc.DoctorId);
    }

    // ------------------------------------------------------------------ consultation record

    [Fact]
    public async Task Consultation_record_text_can_be_edited_and_the_billing_fields_are_ignored()
    {
        var cid = await AddConsultationAsync();
        await Add(ConsultationReq(cid));
        var rec = await RecordIdForConsultationAsync(cid);

        var req = new UpdateMedicalRecordRequestDto
        {
            PatientId = Pat.PatientId, MedicalRecordId = rec, ConsultationId = cid,
            Diagnosis = "bronchitis", Prescription = "antibiotic", RecordNotes = "follow up in a week",
            CustomConsultationPrice = 999m, Items = [It(Bandage, 1)]
        };
        var r = await Edit(req);

        r.ConsultationId.ShouldBe(cid);
        var saved = await LoadRecordAsync(rec);
        saved.Diagnosis.ShouldBe("bronchitis");
        saved.Prescription.ShouldBe("antibiotic");
        saved.Notes.ShouldBe("follow up in a week");
        await using var db = Db();
        (await db.Payments.CountAsync(p => p.ConsultationId == cid)).ShouldBe(0);
        (await db.AppointmentItems.CountAsync(i => i.AppointmentId != Appt)).ShouldBe(0);
    }

    [Fact]
    public async Task Consultation_NotFound_for_a_Rejected_consultation_on_edit()
    {
        var cid = await AddConsultationAsync();
        await Add(ConsultationReq(cid));
        var rec = await RecordIdForConsultationAsync(cid);
        await using (var db = Db()) { (await db.Consultations.SingleAsync(c => c.Id == cid)).Status = ConsultationStatus.Rejected; await db.SaveChangesAsync(); }

        await ShouldFailWith("Consultation.NotFound", () => Edit(new UpdateMedicalRecordRequestDto { PatientId = Pat.PatientId, MedicalRecordId = rec, ConsultationId = cid }));
    }

    [Fact]
    public async Task Record_NotFound_when_the_consultation_does_not_match_the_record()
    {
        var doc2 = await C.AddDoctorAsync();
        var cid = await AddConsultationAsync(doctorId: doc2.DoctorId);
        await ShouldFailWith("Medical.NotFound", () => Edit(new UpdateMedicalRecordRequestDto { PatientId = Pat.PatientId, MedicalRecordId = _record, ConsultationId = cid }));
    }
}

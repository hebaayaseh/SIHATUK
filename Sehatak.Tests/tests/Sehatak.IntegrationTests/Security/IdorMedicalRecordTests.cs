using Sehatak.Application.Common;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.MedicalRecordDto;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Sehatak.IntegrationTests.MedicalRecords;
using Shouldly;

namespace Sehatak.IntegrationTests.Security;

/// <summary>
/// A doctor may only touch the medical data of patients he really treats (an appointment or consultation with him).
/// Another doctor in the same center is a stranger: he must neither read nor edit.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class IdorMedicalRecordTests(SqlServerFixture fx) : MedicalRecordTestBase(fx)
{
    private DoctorInfo _stranger = null!;
    private int _record;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _stranger = await C.AddDoctorAsync();
        await Add(AppointmentReq());
        _record = await RecordIdForAppointmentAsync(Appt);
    }

    private static PagedRequest Page() => new() { PageNumber = 1, PageSize = 10 };

    [Fact]
    public async Task The_treating_doctor_can_read_the_record_and_the_history()
    {
        (await NewRecordService().GetMedicalRecordByIdAsync(C.Id, Doc.UserId, _record)).ShouldNotBeNull();
        (await NewRecordService().GetPatientMedicalHistoryAsync(C.Id, Doc.UserId, Pat.PatientId, Page())).Items.ShouldNotBeEmpty();
    }


    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_stranger_doctor_cannot_edit_a_record_and_nothing_changes()
    {
        var req = new UpdateMedicalRecordRequestDto
        {
            PatientId = Pat.PatientId, MedicalRecordId = _record, AppointmentId = Appt, Diagnosis = "HACKED"
        };

        await Should.ThrowAsync<BusinessException>(() => Edit(req, _stranger.UserId));

        await using var db = Db();
        (await db.MedicalRecords.AsNoTracking().SingleAsync(r => r.Id == _record)).Diagnosis.ShouldBe("flu");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_record_cannot_be_edited_with_another_patient_id_in_the_request()
    {
        var other = await C.AddPatientAsync();
        var req = new UpdateMedicalRecordRequestDto
        {
            PatientId = other.PatientId, MedicalRecordId = _record, AppointmentId = Appt, Diagnosis = "MIXED"
        };

        await Should.ThrowAsync<BusinessException>(() => Edit(req));

        await using var db = Db();
        (await db.MedicalRecords.AsNoTracking().SingleAsync(r => r.Id == _record)).Diagnosis.ShouldBe("flu");
    }

    [Fact]
    public async Task A_doctor_with_a_consultation_for_the_patient_is_allowed_to_read_the_history()
    {
        await AddConsultationAsync(ConsultationStatus.Accepted, Pat.PatientId, _stranger.DoctorId);

        (await NewRecordService().GetPatientMedicalHistoryAsync(C.Id, _stranger.UserId, Pat.PatientId, Page())).ShouldNotBeNull();
    }
}

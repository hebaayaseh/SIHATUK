using Sehatak.Application.DTOs.MedicalRecordDto;
using Sehatak.Application.Validators.Common;
using Sehatak.Application.Validators.MedicalRecord;
using Shouldly;
using static Sehatak.IntegrationTests.Validators.ValidatorTestHelpers;

namespace Sehatak.IntegrationTests.Validators;

public class MedicalRecordValidatorTests
{
    private static List<MedicalRecordItemDto> Items(params (int id, int qty)[] x) => x.Select(i => new MedicalRecordItemDto { Id = i.id, Quantity = i.qty }).ToList();

    // ---------- item ----------

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public void A_quantity_from_1_to_1000_is_valid(int qty) =>
        new MedicalRecordItemDtoValidator().ShouldBeValid(new MedicalRecordItemDto { Id = 1, Quantity = qty });

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Any_other_quantity_is_refused(int qty) =>
        new MedicalRecordItemDtoValidator().ShouldFail(new MedicalRecordItemDto { Id = 1, Quantity = qty }, "Quantity", ValidationKeys.InvalidQuantity);

    [Fact]
    public void An_item_needs_a_positive_id() =>
        new MedicalRecordItemDtoValidator().ShouldFail(new MedicalRecordItemDto { Id = 0, Quantity = 1 }, "Id", ValidationKeys.InvalidId);

    // ---------- create ----------

    private static MedicalRecordDetailRequestDto ForAppointment() => new() { PatientId = 1, AppointmentId = 2 };
    private readonly MedicalRecordDetailRequestDtoValidator _create = new();

    [Fact] public void A_record_for_an_appointment_is_valid() => _create.ShouldBeValid(ForAppointment());
    [Fact] public void A_record_for_a_consultation_is_valid() => _create.ShouldBeValid(new MedicalRecordDetailRequestDto { PatientId = 1, ConsultationId = 3 });

    [Fact]
    public void A_record_belongs_to_exactly_one_of_an_appointment_or_a_consultation()
    {
        _create.ShouldFail(new MedicalRecordDetailRequestDto { PatientId = 1 }, "AppointmentId", ValidationKeys.InvalidValue);
        _create.ShouldFail(new MedicalRecordDetailRequestDto { PatientId = 1, AppointmentId = 2, ConsultationId = 3 }, "AppointmentId", ValidationKeys.InvalidValue);
    }

    [Fact]
    public void The_ids_that_are_sent_must_be_positive()
    {
        _create.ShouldFail(ForAppointment().With("PatientId", 0), "PatientId", ValidationKeys.InvalidId);
        _create.ShouldFail(ForAppointment().With("AppointmentId", 0), "AppointmentId", ValidationKeys.InvalidId);
        _create.ShouldFail(new MedicalRecordDetailRequestDto { PatientId = 1, ConsultationId = -1 }, "ConsultationId", ValidationKeys.InvalidId);
    }

    [Fact]
    public void Text_fields_have_limits()
    {
        _create.ShouldBeValid(ForAppointment().With("Prescription", Long(500)).With("Diagnosis", Long(1000)));
        _create.ShouldFail(ForAppointment().With("Prescription", Long(501)), "Prescription", ValidationKeys.TooLong);
        _create.ShouldFail(ForAppointment().With("Diagnosis", Long(1001)), "Diagnosis", ValidationKeys.TooLong);
    }

    [Fact]
    public void The_consultation_cost_is_a_valid_amount_or_absent()
    {
        _create.ShouldBeValid(ForAppointment().With("ConsultationCost", null));
        _create.ShouldBeValid(ForAppointment().With("ConsultationCost", 0m));
        _create.ShouldFail(ForAppointment().With("ConsultationCost", -1m), "ConsultationCost", ValidationKeys.NegativeAmount);
        _create.ShouldFail(ForAppointment().With("ConsultationCost", 10.123m), "ConsultationCost", ValidationKeys.InvalidValue);
        _create.ShouldFail(ForAppointment().With("ConsultationCost", 100_000_000m), "ConsultationCost", ValidationKeys.AmountTooLarge);
    }

    [Fact]
    public void Items_are_limited_to_100_without_duplicates_and_each_one_is_checked()
    {
        _create.ShouldBeValid(ForAppointment().With("Items", Items(Enumerable.Range(1, 100).Select(i => (i, 1)).ToArray())));
        _create.ShouldFail(ForAppointment().With("Items", Items(Enumerable.Range(1, 101).Select(i => (i, 1)).ToArray())), "Items", ValidationKeys.ListTooLarge);
        _create.ShouldFail(ForAppointment().With("Items", Items((5, 1), (5, 2))), "Items", ValidationKeys.DuplicateItems);
        _create.ShouldFail(ForAppointment().With("Items", Items((5, 0))), "Items[0].Quantity", ValidationKeys.InvalidQuantity);
    }

    /// <summary>Prescription and Diagnosis are limited, but Notes (stored on the same record) is not.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public void The_notes_field_has_a_length_limit() =>
        _create.ShouldFail(ForAppointment().With("Notes", Long(100_000)), "Notes", ValidationKeys.TooLong);

    // ---------- update ----------

    private static UpdateMedicalRecordRequestDto Update() => new() { PatientId = 1, MedicalRecordId = 2, Diagnosis = "d" };
    private readonly UpdateMedicalRecordRequestDtoValidator _update = new();

    [Fact] public void An_update_that_changes_one_field_is_valid() => _update.ShouldBeValid(Update());

    [Fact]
    public void An_update_that_changes_nothing_is_refused()
    {
        _update.ShouldFail(new UpdateMedicalRecordRequestDto { PatientId = 1, MedicalRecordId = 2 }, "request", ValidationKeys.AtLeastOneField);
        _update.ShouldFail(new UpdateMedicalRecordRequestDto { PatientId = 1, MedicalRecordId = 2, Items = new(), RemoveItemIds = new() }, "request", ValidationKeys.AtLeastOneField);
    }

    [Fact]
    public void Any_single_change_counts()
    {
        UpdateMedicalRecordRequestDto Only(Action<UpdateMedicalRecordRequestDto> set) { var d = new UpdateMedicalRecordRequestDto { PatientId = 1, MedicalRecordId = 2 }; set(d); return d; }

        _update.ShouldBeValid(Only(d => d.Prescription = "p"));
        _update.ShouldBeValid(Only(d => d.PaymentNotes = "n"));
        _update.ShouldBeValid(Only(d => d.RecordNotes = "n"));
        _update.ShouldBeValid(Only(d => d.CustomConsultationPrice = 0m));
        _update.ShouldBeValid(Only(d => d.Items = Items((1, 1))));
        _update.ShouldBeValid(Only(d => d.RemoveItemIds = new() { 1 }));
    }

    [Fact]
    public void A_sent_prescription_or_note_cannot_be_blank_and_is_limited_to_500()
    {
        _update.ShouldFail(Update().With("Prescription", ""), "Prescription", ValidationKeys.Required);
        _update.ShouldFail(Update().With("Prescription", Long(501)), "Prescription", ValidationKeys.TooLong);
        _update.ShouldFail(Update().With("RecordNotes", "  "), "RecordNotes", ValidationKeys.Required);
        _update.ShouldFail(Update().With("RecordNotes", Long(501)), "RecordNotes", ValidationKeys.TooLong);
        _update.ShouldFail(Update().With("PaymentNotes", Long(501)), "PaymentNotes", ValidationKeys.TooLong);
        _update.ShouldFail(Update().With("Diagnosis", Long(1001)), "Diagnosis", ValidationKeys.TooLong);
    }

    [Fact]
    public void An_update_checks_ids_price_and_links()
    {
        _update.ShouldFail(Update().With("PatientId", 0), "PatientId", ValidationKeys.InvalidId);
        _update.ShouldFail(Update().With("MedicalRecordId", 0), "MedicalRecordId", ValidationKeys.InvalidId);
        _update.ShouldFail(Update().With("CustomConsultationPrice", -5m), "CustomConsultationPrice", ValidationKeys.NegativeAmount);
        _update.ShouldFail(Update().With("AppointmentId", 1).With("ConsultationId", 2), "AppointmentId", ValidationKeys.InvalidValue);
        _update.ShouldBeValid(Update().With("AppointmentId", 1));
        _update.ShouldBeValid(Update());     // neither link: the record keeps its own
    }

    [Fact]
    public void An_update_checks_item_lists()
    {
        _update.ShouldFail(Update().With("Items", Items((1, 1), (1, 1))), "Items", ValidationKeys.DuplicateItems);
        _update.ShouldFail(Update().With("Items", Items((1, 0))), "Items[0].Quantity", ValidationKeys.InvalidQuantity);
        _update.ShouldFail(Update().With("RemoveItemIds", new List<int> { 0 }), "RemoveItemIds[0]", ValidationKeys.InvalidId);
    }
}

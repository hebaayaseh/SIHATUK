using Sehatak.Application.DTOs.LabDto;
using Sehatak.Application.Validators.Common;
using Sehatak.Application.Validators.Lab;
using Shouldly;
using static Sehatak.IntegrationTests.Validators.ValidatorTestHelpers;

namespace Sehatak.IntegrationTests.Validators;

public class LabValidatorTests
{
    private static List<LabRequestItemsSummaryDto> Items(params int[] ids) => ids.Select(i => new LabRequestItemsSummaryDto { Id = i }).ToList();

    // ---------- create ----------

    private static CreateLabRequestDto Create() => new() { PatientId = 1, AppointmentId = 2, LabItems = Items(10, 11) };

    [Fact]
    public void A_doctor_lab_request_with_items_is_valid() => new CreateLabRequestDtoValidator().ShouldBeValid(Create());

    [Fact]
    public void A_doctor_lab_request_needs_a_patient_and_an_appointment()
    {
        var v = new CreateLabRequestDtoValidator();

        v.ShouldFail(Create().With("PatientId", 0), "PatientId", ValidationKeys.InvalidId);
        v.ShouldFail(Create().With("AppointmentId", 0), "AppointmentId", ValidationKeys.InvalidId);
    }

    [Fact]
    public void The_item_list_must_exist_and_be_non_empty_and_at_most_50_without_duplicates()
    {
        var v = new CreateLabRequestDtoValidator();

        v.ShouldFail(Create().With("LabItems", null), "LabItems", ValidationKeys.ListEmpty);
        v.ShouldFail(Create().With("LabItems", Items()), "LabItems", ValidationKeys.ListEmpty);
        v.ShouldFail(Create().With("LabItems", Items(Enumerable.Range(1, 51).ToArray())), "LabItems", ValidationKeys.ListTooLarge);
        v.ShouldBeValid(Create().With("LabItems", Items(Enumerable.Range(1, 50).ToArray())));
        v.ShouldFail(Create().With("LabItems", Items(5, 5)), "LabItems", ValidationKeys.DuplicateItems);
    }

    [Fact]
    public void Each_item_id_is_checked_with_its_position_in_the_error()
    {
        new CreateLabRequestDtoValidator()
            .ShouldFail(Create().With("LabItems", Items(10, 0)), "LabItems[1].Id", ValidationKeys.InvalidId);
    }

    [Fact]
    public void The_note_is_limited_to_500()
    {
        var v = new CreateLabRequestDtoValidator();
        v.ShouldBeValid(Create().With("Note", Long(500)));
        v.ShouldFail(Create().With("Note", Long(501)), "Note", ValidationKeys.TooLong);
    }

    [Fact]
    public void The_receptionist_create_needs_no_appointment_but_the_same_item_rules()
    {
        var v = new ReceptionistCreateLabRequestDtoValidator();
        var ok = new ReceptionistCreateLabRequestDto { PatientId = 1, LabItems = Items(1) };

        v.ShouldBeValid(ok);
        v.ShouldFail(ok.With("PatientId", 0), "PatientId", ValidationKeys.InvalidId);
        v.ShouldFail(new ReceptionistCreateLabRequestDto { PatientId = 1, LabItems = Items() }, "LabItems", ValidationKeys.ListEmpty);
        v.ShouldFail(new ReceptionistCreateLabRequestDto { PatientId = 1, LabItems = Items(3, 3) }, "LabItems", ValidationKeys.DuplicateItems);
    }

    // ---------- update ----------

    private static UpdateLabRequestDto Update() => new() { LabRequestId = 1, PatientId = 2, AppointmentId = 3, Note = "n" };

    [Fact]
    public void An_update_must_change_something()
    {
        var v = new UpdateLabRequestDtoValidator();

        v.ShouldBeValid(Update());                                                   // just a note
        v.ShouldBeValid(new UpdateLabRequestDto { LabRequestId = 1, PatientId = 2, AppointmentId = 3, AddLabItems = Items(1) });
        v.ShouldBeValid(new UpdateLabRequestDto { LabRequestId = 1, PatientId = 2, AppointmentId = 3, RemoveLabItems = new() { 1 } });
        v.ShouldFail(new UpdateLabRequestDto { LabRequestId = 1, PatientId = 2, AppointmentId = 3 }, "request", ValidationKeys.AtLeastOneField);
        v.ShouldFail(new UpdateLabRequestDto { LabRequestId = 1, PatientId = 2, AppointmentId = 3, AddLabItems = Items(), RemoveLabItems = new() }, "request", ValidationKeys.AtLeastOneField);
    }

    [Fact]
    public void An_update_checks_its_ids()
    {
        var v = new UpdateLabRequestDtoValidator();

        v.ShouldFail(Update().With("LabRequestId", 0), "LabRequestId", ValidationKeys.InvalidId);
        v.ShouldFail(Update().With("PatientId", 0), "PatientId", ValidationKeys.InvalidId);
        v.ShouldFail(Update().With("AppointmentId", 0), "AppointmentId", ValidationKeys.InvalidId);
    }

    [Fact]
    public void An_update_checks_added_and_removed_items()
    {
        var v = new UpdateLabRequestDtoValidator();

        v.ShouldFail(Update().With("AddLabItems", Items(4, 4)), "AddLabItems", ValidationKeys.DuplicateItems);
        v.ShouldFail(Update().With("AddLabItems", Items(Enumerable.Range(1, 51).ToArray())), "AddLabItems", ValidationKeys.ListTooLarge);
        v.ShouldFail(Update().With("AddLabItems", Items(0)), "AddLabItems[0].Id", ValidationKeys.InvalidId);
        v.ShouldFail(Update().With("RemoveLabItems", new List<int> { 0 }), "RemoveLabItems[0]", ValidationKeys.InvalidId);
        v.ShouldFail(Update().With("RemoveLabItems", new List<int> { 7, 7 }), "RemoveLabItems", ValidationKeys.DuplicateItems);
    }

    [Fact]
    public void The_receptionist_update_has_the_same_checks_but_no_appointment()
    {
        var v = new ReceptionistUpdateLabRequestDtoValidator();
        var ok = new ReceptionistUpdateLabRequestDto { LabRequestId = 1, PatientId = 2, Note = "n" };

        v.ShouldBeValid(ok);
        v.ShouldFail(new ReceptionistUpdateLabRequestDto { LabRequestId = 1, PatientId = 2 }, "request", ValidationKeys.AtLeastOneField);
        v.ShouldFail(ok.With("LabRequestId", 0), "LabRequestId", ValidationKeys.InvalidId);
        v.ShouldFail(ok.With("RemoveLabItems", new List<int> { -1 }), "RemoveLabItems[0]", ValidationKeys.InvalidId);
        v.ShouldFail(ok.With("AddLabItems", Items(2, 2)), "AddLabItems", ValidationKeys.DuplicateItems);
    }

    /// <summary>The doctor's update refuses the same id twice in RemoveLabItems; the receptionist's copy of the rule is missing.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public void The_receptionist_update_refuses_the_same_removed_item_twice()
    {
        var ok = new ReceptionistUpdateLabRequestDto { LabRequestId = 1, PatientId = 2, RemoveLabItems = new() { 7, 7 } };

        new ReceptionistUpdateLabRequestDtoValidator().ShouldFail(ok, "RemoveLabItems", ValidationKeys.DuplicateItems);
    }

    // ---------- results ----------

    private static LabResultItemDto Result(decimal value = 5.5m) => new() { LabRequestItemId = 1, ResultValue = value };

    [Theory]
    [InlineData("0")]
    [InlineData("5.123")]
    [InlineData("9999999")]
    public void These_result_values_are_valid(string value) =>
        new LabResultItemDtoValidator().ShouldBeValid(Result(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void A_result_value_is_not_negative_not_huge_and_has_at_most_three_decimals()
    {
        var v = new LabResultItemDtoValidator();

        v.ShouldFail(Result(-0.001m), "ResultValue", ValidationKeys.NegativeAmount);
        v.ShouldFail(Result(10_000_000m), "ResultValue", ValidationKeys.AmountTooLarge);
        v.ShouldFail(Result(1.2345m), "ResultValue", ValidationKeys.InvalidValue);
    }

    [Fact]
    public void A_result_needs_an_item_id_and_may_carry_a_document()
    {
        var v = new LabResultItemDtoValidator();

        v.ShouldFail(Result().With("LabRequestItemId", 0), "LabRequestItemId", ValidationKeys.InvalidId);
        v.ShouldBeValid(Result().With("ResultFileUrl", FormFileOf("r.pdf", "application/pdf")));
        v.ShouldFail(Result().With("ResultFileUrl", FormFileOf("r.zip", "application/zip")), "ResultFileUrl", ValidationKeys.FileTypeNotAllowed);
    }

    [Fact]
    public void An_upload_needs_results_without_duplicates_and_at_most_50()
    {
        var v = new UploadLabResultRequestDtoValidator();
        LabResultItemDto Item(int id) => new() { LabRequestItemId = id, ResultValue = 1 };

        v.ShouldBeValid(new UploadLabResultRequestDto { LabRequestId = 1, Results = new() { Item(1), Item(2) } });
        v.ShouldFail(new UploadLabResultRequestDto { LabRequestId = 0, Results = new() { Item(1) } }, "LabRequestId", ValidationKeys.InvalidId);
        v.ShouldFail(new UploadLabResultRequestDto { LabRequestId = 1, Results = new() }, "Results", ValidationKeys.ListEmpty);
        v.ShouldFail(new UploadLabResultRequestDto { LabRequestId = 1, Results = null! }, "Results", ValidationKeys.ListEmpty);
        v.ShouldFail(new UploadLabResultRequestDto { LabRequestId = 1, Results = new() { Item(3), Item(3) } }, "Results", ValidationKeys.DuplicateItems);
        v.ShouldFail(new UploadLabResultRequestDto { LabRequestId = 1, Results = Enumerable.Range(1, 51).Select(Item).ToList() }, "Results", ValidationKeys.ListTooLarge);
        v.ShouldFail(new UploadLabResultRequestDto { LabRequestId = 1, Results = new() { new() { LabRequestItemId = 1, ResultValue = -1 } } }, "Results[0].ResultValue", ValidationKeys.NegativeAmount);
    }
}

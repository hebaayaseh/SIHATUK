using Sehatak.Application.DTOs.ConfirmPaymentDto;
using Sehatak.Application.DTOs.PaymentDto;
using Sehatak.Application.DTOs.RecordPaymentRequestDto;
using Sehatak.Application.Validators.Common;
using Sehatak.Application.Validators.Payment;
using Sehatak.Domain.Enums.PaymentEnums;
using Shouldly;
using static Sehatak.IntegrationTests.Validators.ValidatorTestHelpers;

namespace Sehatak.IntegrationTests.Validators;

public class PaymentRequestValidatorTests
{
    private readonly PaymentRequestDtoValidator _v = new();

    private static PaymentRequestDto Online() => new()
    {
        Method = PaymentMethod.online,
        Type = PaymentType.Appointment,
        Status = PaymentStatus.Pending,
        ReferenceNumber = "REF-1"
    };

    [Fact] public void An_online_payment_with_a_reference_is_valid() => _v.ShouldBeValid(Online());

    [Fact]
    public void A_cash_payment_needs_no_reference_or_receipt() =>
        _v.ShouldBeValid(new PaymentRequestDto { Method = PaymentMethod.cash, Type = PaymentType.Appointment, Status = PaymentStatus.Paid });

    [Theory]
    [InlineData(PaymentMethod.card)]
    [InlineData(PaymentMethod.online)]
    public void Any_non_cash_payment_needs_a_reference_or_a_receipt(PaymentMethod method)
    {
        var dto = Online().With("Method", method).With("ReferenceNumber", null);
        _v.ShouldFail(dto, "ReferenceNumber", ValidationKeys.AtLeastOneField);

        _v.ShouldBeValid(dto.With("ReceiptImageUrl", FormFileOf("r.pdf", "application/pdf")));
    }

    [Fact]
    public void A_blank_reference_does_not_count_as_a_reference() =>
        _v.ShouldFail(Online().With("ReferenceNumber", "   "), "ReferenceNumber", ValidationKeys.AtLeastOneField);

    [Fact]
    public void Method_type_and_status_must_be_real_values()
    {
        _v.ShouldFail(Online().With("Method", (PaymentMethod)0), "Method", ValidationKeys.InvalidEnum);
        _v.ShouldFail(Online().With("Method", (PaymentMethod)9), "Method", ValidationKeys.InvalidEnum);
        _v.ShouldFail(Online().With("Type", (PaymentType)0), "Type", ValidationKeys.InvalidEnum);
        _v.ShouldFail(Online().With("Type", (PaymentType)7), "Type", ValidationKeys.InvalidEnum);
        _v.ShouldFail(Online().With("Status", (PaymentStatus)0), "Status", ValidationKeys.InvalidEnum);
        _v.ShouldFail(Online().With("Status", (PaymentStatus)5), "Status", ValidationKeys.InvalidEnum);
    }

    [Fact]
    public void An_omitted_type_is_refused_because_it_has_no_default()
    {
        var dto = new PaymentRequestDto { ReferenceNumber = "REF-1" };   // Type left at 0
        _v.ShouldFail(dto, "Type", ValidationKeys.InvalidEnum);
    }

    [Fact]
    public void Reference_and_notes_have_length_limits()
    {
        _v.ShouldFail(Online().With("ReferenceNumber", Long(101)), "ReferenceNumber", ValidationKeys.TooLong);
        _v.ShouldBeValid(Online().With("ReferenceNumber", Long(100)));
        _v.ShouldFail(Online().With("Notes", Long(501)), "Notes", ValidationKeys.TooLong);
        _v.ShouldBeValid(Online().With("Notes", Long(500)));
    }

    [Fact]
    public void The_receipt_must_be_an_image_or_a_pdf_under_5_MB()
    {
        _v.ShouldBeValid(Online().With("ReceiptImageUrl", FormFileOf("r.png", "image/png")));
        _v.ShouldFail(Online().With("ReceiptImageUrl", FormFileOf("r.exe", "application/octet-stream")), "ReceiptImageUrl", ValidationKeys.FileTypeNotAllowed);
        _v.ShouldFail(Online().With("ReceiptImageUrl", FormFileOf("r.pdf", "application/pdf", ValidationRules.MaxDocumentBytes + 1)), "ReceiptImageUrl", ValidationKeys.FileTooLarge);
    }
}

public class CollectPaymentValidatorTests
{
    private readonly CollectPaymentRequestDtoValidator _v = new();

    [Fact] public void A_method_alone_is_enough() => _v.ShouldBeValid(new CollectPaymentRequestDto { Method = PaymentMethod.cash });

    [Fact]
    public void The_method_must_be_a_real_value()
    {
        _v.ShouldFail(new CollectPaymentRequestDto { Method = 0 }, "Method", ValidationKeys.InvalidEnum);
        _v.ShouldFail(new CollectPaymentRequestDto { Method = (PaymentMethod)10 }, "Method", ValidationKeys.InvalidEnum);
    }

    [Fact]
    public void The_note_is_limited_to_500()
    {
        _v.ShouldBeValid(new CollectPaymentRequestDto { Method = PaymentMethod.cash, Note = Long(500) });
        _v.ShouldFail(new CollectPaymentRequestDto { Method = PaymentMethod.cash, Note = Long(501) }, "Note", ValidationKeys.TooLong);
    }
}

public class RecordPaymentValidatorTests
{
    private readonly RecordPaymentRequestDtoValidator _v = new();

    [Theory]
    [InlineData("cash")]
    [InlineData("Cash")]
    [InlineData(" CASH ")]
    public void Cash_needs_nothing_else(string method) =>
        _v.ShouldBeValid(new recordPaymentRequestDto { PaymentMethod = method });

    [Theory]
    [InlineData("card")]
    [InlineData("online")]
    [InlineData("bank transfer")]
    [InlineData("BankTransfer")]
    [InlineData("cheque")]
    public void The_other_known_methods_need_a_reference_or_a_receipt(string method)
    {
        var dto = new recordPaymentRequestDto { PaymentMethod = method };
        _v.ShouldFail(dto, "ReferenceNumber", ValidationKeys.AtLeastOneField);

        _v.ShouldBeValid(new recordPaymentRequestDto { PaymentMethod = method, ReferenceNumber = "R1" });
        _v.ShouldBeValid(new recordPaymentRequestDto { PaymentMethod = method, ReceiptImage = FormFileOf("r.jpg", "image/jpeg") });
    }

    [Theory]
    [InlineData("bitcoin")]
    [InlineData("credit")]
    [InlineData("0")]
    public void An_unknown_method_is_refused(string method) =>
        _v.ShouldFail(new recordPaymentRequestDto { PaymentMethod = method, ReferenceNumber = "R1" }, "PaymentMethod", ValidationKeys.InvalidValue);

    [Fact]
    public void The_method_is_required_and_limited()
    {
        _v.ShouldFail(new recordPaymentRequestDto { PaymentMethod = "" }, "PaymentMethod", ValidationKeys.Required);
        _v.ShouldFail(new recordPaymentRequestDto { PaymentMethod = Long(51), ReferenceNumber = "R" }, "PaymentMethod", ValidationKeys.TooLong);
    }

    [Fact]
    public void Reference_and_notes_have_length_limits()
    {
        _v.ShouldFail(new recordPaymentRequestDto { PaymentMethod = "cash", ReferenceNumber = Long(101) }, "ReferenceNumber", ValidationKeys.TooLong);
        _v.ShouldFail(new recordPaymentRequestDto { PaymentMethod = "cash", Notes = Long(501) }, "Notes", ValidationKeys.TooLong);
    }
}

public class PaymentRequestExistValidatorTests
{
    private readonly PaymentRequestExistValidator _v = new();

    [Fact]
    public void A_center_or_a_subscription_is_enough()
    {
        _v.ShouldBeValid(new PaymentRequestExist { centerId = 1, PaymentMethod = "cash" });
        _v.ShouldBeValid(new PaymentRequestExist { SubscriptionId = 1, PaymentMethod = "card" });
    }

    [Fact]
    public void Neither_is_refused() =>
        _v.ShouldFail(new PaymentRequestExist { PaymentMethod = "cash" }, "centerId", ValidationKeys.AtLeastOneField);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ids_that_are_sent_must_be_positive(int id)
    {
        _v.ShouldFail(new PaymentRequestExist { centerId = id, SubscriptionId = 1, PaymentMethod = "cash" }, "centerId", ValidationKeys.InvalidId);
        _v.ShouldFail(new PaymentRequestExist { centerId = 1, SubscriptionId = id, PaymentMethod = "cash" }, "SubscriptionId", ValidationKeys.InvalidId);
    }

    [Theory]
    [InlineData("cash")]
    [InlineData("Card")]
    [InlineData(" ONLINE ")]
    public void The_payment_methods_we_have_are_accepted_in_any_case(string m) =>
    _v.ShouldBeValid(new PaymentRequestExist { centerId = 1, PaymentMethod = m });

    [Theory]
    [InlineData("Wallet")]
    [InlineData("Bank Transfer")]
    [InlineData("cheque")]
    [InlineData("1")]
    public void Anything_else_is_refused(string m) =>
        _v.ShouldFail(new PaymentRequestExist { centerId = 1, PaymentMethod = m }, "PaymentMethod", ValidationKeys.InvalidValue);

    [Fact]
    public void The_method_is_required_and_limited_to_50()
    {
        _v.ShouldFail(new PaymentRequestExist { centerId = 1, PaymentMethod = "" }, "PaymentMethod", ValidationKeys.Required);
        _v.ShouldFail(new PaymentRequestExist { centerId = 1, PaymentMethod = Long(51) }, "PaymentMethod", ValidationKeys.TooLong);
    }

    /// <summary>By design: whoever records the payment picks cash, card or online and no reference is forced here.</summary>
    [Theory]
    [InlineData("cash")]
    [InlineData("card")]
    [InlineData("online")]
    public void Any_method_is_accepted_without_a_reference(string method) =>
        _v.ShouldBeValid(new PaymentRequestExist { centerId = 1, PaymentMethod = method });
}

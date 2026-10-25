using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Payments;

/// <summary>BillPaymentService: the patient's payment history, split by appointments / labs / consultations.</summary>
[Collection(SqlServerCollection.Name)]
public class BillPaymentTests(SqlServerFixture fx) : PaymentTestBase(fx)
{
    private static readonly PagedRequest Page = new() { PageNumber = 1, PageSize = 10 };

    // ------------------------------------------------------------------ appointments

    [Fact]
    public async Task Appointment_bills_show_amount_status_method_and_the_billed_items_newest_first()
    {
        var xray = await AddServiceAsync("X-ray", 40m, ServiceType.MedicalSupply);
        var a1 = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.Completed);
        var a2 = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date.AddDays(7), S0900, AppointmentStatus.Completed);
        await AddAppointmentItemAsync(a2, xray, quantity: 2, unitPrice: 40m);
        var older = await AddPaymentAsync(Pat.PatientId, 100m, PaymentType.Appointment, PaymentStatus.Paid, appointmentId: a1,
            method: PaymentMethod.cash, paidAtUtc: DateTime.UtcNow.AddDays(-2));
        var newer = await AddPaymentAsync(Pat.PatientId, 80m, PaymentType.Appointment, PaymentStatus.Paid, appointmentId: a2,
            method: PaymentMethod.card, paidAtUtc: DateTime.UtcNow.AddDays(-1));

        var res = await NewBillService().AppointmentsBillPayment(C.Id, Pat.UserId, Page, null);

        res.TotalCount.ShouldBe(2);
        res.Items.Select(i => i.PaymentId).ShouldBe(new[] { newer, older });

        var first = res.Items[0];
        first.AppointmentId.ShouldBe(a2);
        first.PatientId.ShouldBe(Pat.PatientId);
        first.Amount.ShouldBe(80m);
        first.BillPaymentStatus.ShouldBe("Paid");
        first.BillPaymentType.ShouldBe("Appointment");
        first.Method.ShouldBe("card");
        first.PatientName.ShouldNotBeNullOrWhiteSpace();
        var item = first.Items!.Single();
        item.ServiceName.ShouldBe("X-ray");
        item.Quantity.ShouldBe(2);
        item.UnitPrice.ShouldBe(40m);
        item.TotalPrice.ShouldBe(80m);

        res.Items[1].Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Appointment_bills_ignore_other_payment_types_and_other_patients()
    {
        var other = await C.AddPatientAsync();
        var mine = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.Completed);
        var theirs = await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S1000, AppointmentStatus.Completed);
        var lab = await AddLabRequestAsync(Pat.PatientId, LabRequestStatus.Processing);
        var myPayment = await AddPaymentAsync(Pat.PatientId, 10m, PaymentType.Appointment, PaymentStatus.Paid, appointmentId: mine);
        await AddPaymentAsync(other.PatientId, 20m, PaymentType.Appointment, PaymentStatus.Paid, appointmentId: theirs);
        await AddPaymentAsync(Pat.PatientId, 30m, PaymentType.Lab, PaymentStatus.Paid, labRequestId: lab);
        await AddPaymentAsync(Pat.PatientId, 40m, PaymentType.Consultation, PaymentStatus.Paid);

        var res = await NewBillService().AppointmentsBillPayment(C.Id, Pat.UserId, Page, null);

        res.Items.Select(i => i.PaymentId).ShouldBe(new[] { myPayment });
    }

    [Fact]
    public async Task Appointment_bills_for_a_child_are_the_childs_not_the_parents()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var parentAppt = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.Completed);
        var childAppt = await C.AddAppointmentAsync(Doc.DoctorId, sub.PatientId, Date, S1000, AppointmentStatus.Completed);
        await AddPaymentAsync(Pat.PatientId, 10m, PaymentType.Appointment, PaymentStatus.Paid, appointmentId: parentAppt);
        var childPayment = await AddPaymentAsync(sub.PatientId, 20m, PaymentType.Appointment, PaymentStatus.Paid, appointmentId: childAppt);

        var res = await NewBillService().AppointmentsBillPayment(C.Id, Pat.UserId, Page, sub.PatientId);

        var only = res.Items.Single();
        only.PaymentId.ShouldBe(childPayment);
        only.PatientId.ShouldBe(sub.PatientId);
        only.PatientName.ShouldBe("P Test");
    }

    [Fact]
    public async Task Appointment_bills_are_paged()
    {
        for (var i = 0; i < 3; i++)
        {
            var appt = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date.AddDays(7 * i), S0900, AppointmentStatus.Completed);
            await AddPaymentAsync(Pat.PatientId, 10m, PaymentType.Appointment, PaymentStatus.Paid, appointmentId: appt,
                paidAtUtc: DateTime.UtcNow.AddDays(-i));
        }

        var res = await NewBillService().AppointmentsBillPayment(C.Id, Pat.UserId, new PagedRequest { PageNumber = 2, PageSize = 2 }, null);

        res.TotalCount.ShouldBe(3);
        res.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Appointment_bills_is_empty_when_there_are_no_payments()
    {
        var res = await NewBillService().AppointmentsBillPayment(C.Id, Pat.UserId, Page, null);

        res.TotalCount.ShouldBe(0);
        res.Items.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ labs

    [Fact]
    public async Task Lab_bills_show_the_lab_items_with_their_prices()
    {
        var cbc = await AddServiceAsync("CBC", 30m);
        var vitd = await AddServiceAsync("Vitamin D", 20m);
        var lab = await AddLabRequestAsync(Pat.PatientId, LabRequestStatus.Processing, (cbc, 30m, true), (vitd, 20m, true));
        var payment = await AddPaymentAsync(Pat.PatientId, 50m, PaymentType.Lab, PaymentStatus.Paid, labRequestId: lab, method: PaymentMethod.card);

        var res = await NewBillService().LabsBillPayment(C.Id, Pat.UserId, Page, null);

        var bill = res.Items.Single();
        bill.PaymentId.ShouldBe(payment);
        bill.LabId.ShouldBe(lab);
        bill.Amount.ShouldBe(50m);
        bill.BillPaymentType.ShouldBe("Lab");
        bill.BillPaymentStatus.ShouldBe("Paid");
        bill.Method.ShouldBe("card");
        bill.Items!.Select(i => (i.ServiceName, i.UnitPrice)).ShouldBe(new[] { ("CBC", 30m), ("Vitamin D", 20m) }, ignoreOrder: true);
    }

    [Fact]
    public async Task Lab_bills_ignore_other_types_and_other_patients()
    {
        var other = await C.AddPatientAsync();
        var s = await AddServiceAsync("CBC", 30m);
        var mineLab = await AddLabRequestAsync(Pat.PatientId, LabRequestStatus.Processing, (s, 30m, true));
        var theirLab = await AddLabRequestAsync(other.PatientId, LabRequestStatus.Processing, (s, 30m, true));
        var mine = await AddPaymentAsync(Pat.PatientId, 30m, PaymentType.Lab, PaymentStatus.Paid, labRequestId: mineLab);
        await AddPaymentAsync(other.PatientId, 30m, PaymentType.Lab, PaymentStatus.Paid, labRequestId: theirLab);
        await AddPaymentAsync(Pat.PatientId, 40m, PaymentType.Consultation, PaymentStatus.Paid);

        var res = await NewBillService().LabsBillPayment(C.Id, Pat.UserId, Page, null);

        res.Items.Select(i => i.PaymentId).ShouldBe(new[] { mine });
    }

    [Fact]
    public async Task Lab_bills_for_a_child()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var s = await AddServiceAsync("CBC", 30m);
        var childLab = await AddLabRequestAsync(sub.PatientId, LabRequestStatus.Processing, (s, 30m, true));
        var payment = await AddPaymentAsync(sub.PatientId, 30m, PaymentType.Lab, PaymentStatus.Paid, labRequestId: childLab);

        var res = await NewBillService().LabsBillPayment(C.Id, Pat.UserId, Page, sub.PatientId);

        res.Items.Single().PaymentId.ShouldBe(payment);
    }

    // ------------------------------------------------------------------ consultations

    [Fact]
    public async Task Consultation_bills_show_the_online_payments_of_the_patient_only()
    {
        var other = await C.AddPatientAsync();
        var mine = await AddPaymentAsync(Pat.PatientId, 60m, PaymentType.Consultation, PaymentStatus.Paid, method: PaymentMethod.online);
        await AddPaymentAsync(other.PatientId, 60m, PaymentType.Consultation, PaymentStatus.Paid);
        await AddPaymentAsync(Pat.PatientId, 10m, PaymentType.Appointment, PaymentStatus.Paid);

        var res = await NewBillService().ConsultationSBillPayment(C.Id, Pat.UserId, Page, null);

        var bill = res.Items.Single();
        bill.PaymentId.ShouldBe(mine);
        bill.Amount.ShouldBe(60m);
        bill.BillPaymentType.ShouldBe("Consultation");
        bill.Method.ShouldBe("online");
        bill.PatientId.ShouldBe(Pat.PatientId);
    }

    [Fact]
    public async Task Consultation_bills_for_a_child()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        await AddPaymentAsync(Pat.PatientId, 60m, PaymentType.Consultation, PaymentStatus.Paid);
        var childPayment = await AddPaymentAsync(sub.PatientId, 45m, PaymentType.Consultation, PaymentStatus.Paid);

        var res = await NewBillService().ConsultationSBillPayment(C.Id, Pat.UserId, Page, sub.PatientId);

        res.Items.Single().PaymentId.ShouldBe(childPayment);
    }

    // ------------------------------------------------------------------ business errors (all three methods)

    [Fact]
    public async Task Center_NotFound_on_every_bill()
    {
        await ShouldFailWith("Center.NotFound", () => NewBillService().AppointmentsBillPayment(999_999, Pat.UserId, Page, null));
        await ShouldFailWith("Center.NotFound", () => NewBillService().LabsBillPayment(999_999, Pat.UserId, Page, null));
        await ShouldFailWith("Center.NotFound", () => NewBillService().ConsultationSBillPayment(999_999, Pat.UserId, Page, null));
    }

    [Fact]
    public async Task Patient_NotFound_on_every_bill()
    {
        await ShouldFailWith("Patient.NotFound", () => NewBillService().AppointmentsBillPayment(C.Id, 999_999, Page, null));
        await ShouldFailWith("Patient.NotFound", () => NewBillService().LabsBillPayment(C.Id, 999_999, Page, null));
        await ShouldFailWith("Patient.NotFound", () => NewBillService().ConsultationSBillPayment(C.Id, 999_999, Page, null));
    }

    /// <summary>Note: this service uses "SubPatient.NotFound"; the appointment services use "SubPatient.NotFoundOrNotOwned".</summary>
    [Fact]
    public async Task SubPatient_NotFound_for_someone_elses_child_on_every_bill()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);

        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => NewBillService().AppointmentsBillPayment(C.Id, Pat.UserId, Page, child.PatientId));
        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => NewBillService().LabsBillPayment(C.Id, Pat.UserId, Page, child.PatientId));
        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => NewBillService().ConsultationSBillPayment(C.Id, Pat.UserId, Page, child.PatientId));
    }
}

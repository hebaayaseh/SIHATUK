#!/usr/bin/env python3
"""Append missing resource entries to Messages.en.resx / Messages.ar.resx.

Idempotent: keys already present are skipped, so it is safe to re-run after
adding more BusinessException keys to the services.
"""
import re
import sys
from pathlib import Path

RES_DIR = Path(sys.argv[1] if len(sys.argv) > 1 else
               "Sehatak.API/Resources")

# key -> (english, arabic)
ENTRIES = {
    # ---------- validation keys used by the FluentValidation validators -------
    "Validation.TooLong": ("The value is too long.", "القيمة أطول من المسموح."),
    "Validation.TooShort": ("The value is too short.", "القيمة أقصر من المسموح."),
    "Validation.InvalidValue": ("The value is not valid.", "القيمة غير صالحة."),
    "Validation.InvalidId": ("The identifier is not valid.", "المعرّف غير صالح."),
    "Validation.InvalidEnum": ("The selected option is not valid.", "الخيار المحدد غير صالح."),
    "Validation.AtLeastOneFieldRequired": ("At least one field must be provided.", "يجب إدخال حقل واحد على الأقل."),
    "Validation.PasswordTooWeak": ("The password must contain an uppercase letter, a lowercase letter and a digit.", "يجب أن تحتوي كلمة المرور على حرف كبير وحرف صغير ورقم."),
    "Validation.PasswordMismatch": ("The passwords do not match.", "كلمتا المرور غير متطابقتين."),
    "Validation.SamePassword": ("The new password must be different from the current one.", "يجب أن تختلف كلمة المرور الجديدة عن الحالية."),
    "Validation.InvalidOtpFormat": ("The verification code must be 6 digits.", "يجب أن يتكون رمز التحقق من 6 أرقام."),
    "Validation.DateInPast": ("The date cannot be in the past.", "لا يمكن أن يكون التاريخ في الماضي."),
    "Validation.DateInFuture": ("The date cannot be in the future.", "لا يمكن أن يكون التاريخ في المستقبل."),
    "Validation.DateTooFar": ("The date is too far ahead.", "التاريخ بعيد جدًا."),
    "Validation.EndTimeBeforeStartTime": ("The end time must be after the start time.", "يجب أن يكون وقت الانتهاء بعد وقت البدء."),
    "Validation.ShiftTooShort": ("The working window is shorter than one slot.", "فترة العمل أقصر من مدة الموعد الواحد."),
    "Validation.InvalidDateOfBirth": ("The date of birth is not valid.", "تاريخ الميلاد غير صالح."),
    "Validation.NegativeAmount": ("The amount cannot be negative.", "لا يمكن أن يكون المبلغ سالبًا."),
    "Validation.AmountTooLarge": ("The amount is too large.", "المبلغ كبير جدًا."),
    "Validation.InvalidPercent": ("The percentage must be between 0 and 100.", "يجب أن تكون النسبة بين 0 و 100."),
    "Validation.InvalidQuantity": ("The quantity is not valid.", "الكمية غير صالحة."),
    "Validation.InvalidRating": ("The rating must be between 1 and 5.", "يجب أن يكون التقييم بين 1 و 5."),
    "Validation.InvalidSlotDuration": ("The slot duration must be between 5 and 480 minutes.", "يجب أن تكون مدة الموعد بين 5 و 480 دقيقة."),
    "Validation.FileRequired": ("A file is required.", "يجب إرفاق ملف."),
    "Validation.FileTooLarge": ("The file exceeds the maximum allowed size.", "حجم الملف يتجاوز الحد المسموح."),
    "Validation.FileTypeNotAllowed": ("This file type is not allowed.", "نوع الملف غير مسموح."),
    "Validation.FileNameInvalid": ("The file name is not valid.", "اسم الملف غير صالح."),
    "Validation.ListEmpty": ("The list cannot be empty.", "القائمة لا يمكن أن تكون فارغة."),
    "Validation.ListTooLarge": ("The list contains too many items.", "القائمة تحتوي على عناصر أكثر من المسموح."),
    "Validation.DuplicateItems": ("The list contains duplicate items.", "القائمة تحتوي على عناصر مكررة."),
    "Validation.InvalidUrl": ("The link is not a valid URL.", "الرابط غير صالح."),
    "Validation.Failed": ("One or more fields are not valid.", "يوجد خطأ في أحد الحقول."),

    # ---------- BusinessException keys thrown but never translated -----------
    "Appointment.AlreadyCheckedIn": ("The patient has already checked in.", "تم تسجيل دخول المريض مسبقًا."),
    "Appointment.AlreadyCheckedOut": ("The patient has already checked out.", "تم تسجيل خروج المريض مسبقًا."),
    "Appointment.AlreadyExists": ("An appointment already exists for this day.", "يوجد موعد محجوز مسبقًا في هذا اليوم."),
    "Appointment.AlreadyFinished": ("The appointment has already finished.", "تم إنهاء الموعد مسبقًا."),
    "Appointment.AlreadyStarted": ("The appointment has already started.", "بدأ الموعد مسبقًا."),
    "Appointment.NoFound": ("Appointment not found.", "الموعد غير موجود."),
    "Appointment.NotCheckedInYet": ("The patient has not checked in yet.", "لم يتم تسجيل دخول المريض بعد."),
    "Appointment.NotStartedYet": ("The appointment has not started yet.", "لم يبدأ الموعد بعد."),
    "Attendance.AlreadyExsist": ("Attendance has already been recorded for this day.", "تم تسجيل الحضور لهذا اليوم مسبقًا."),
    "Attendance.NotFound": ("Attendance record not found.", "سجل الحضور غير موجود."),
    "Auth.InvalidRefreshToken": ("The refresh token is invalid or expired.", "رمز التحديث غير صالح أو منتهي الصلاحية."),
    "Cente.NotFound": ("Center not found.", "المركز غير موجود."),
    "Center.NoFound": ("Center not found.", "المركز غير موجود."),
    "CenterNotFound": ("Center not found.", "المركز غير موجود."),
    "Center.UniqueUrlExists": ("This center URL is already taken.", "رابط المركز مستخدم بالفعل."),
    "CenterRegistration.AdminCreationFailed": ("Creating the center administrator failed.", "فشل إنشاء حساب مدير المركز."),
    "CenterRegistration.NotFound": ("Registration request not found.", "طلب التسجيل غير موجود."),
    "CenterRegistration.PaymentNotConfirmed": ("The registration payment has not been confirmed.", "لم يتم تأكيد دفعة التسجيل."),
    "Consultation.AlreadyRequested": ("A consultation has already been requested.", "تم طلب الاستشارة مسبقًا."),
    "Consultation.CannotCancelAfterConfirmed": ("The consultation cannot be cancelled after confirmation.", "لا يمكن إلغاء الاستشارة بعد تأكيدها."),
    "Consultation.CannotCancelAfterPaymentSubmitted": ("The consultation cannot be cancelled after payment was submitted.", "لا يمكن إلغاء الاستشارة بعد إرسال الدفعة."),
    "Consultation.NotPending": ("The consultation is not pending.", "الاستشارة ليست قيد الانتظار."),
    "Date.Invalid": ("The date is not valid.", "التاريخ غير صالح."),
    "Invalid.Date": ("The date is not valid.", "التاريخ غير صالح."),
    "Doctor.DayAlreadyBlocked": ("This day is already blocked for the doctor.", "تم حجب هذا اليوم للطبيب مسبقًا."),
    "Doctor.DayBlocked": ("The doctor is not available on this day.", "الطبيب غير متاح في هذا اليوم."),
    "Doctor.NoFound": ("Doctor not found.", "الطبيب غير موجود."),
    "DoctorRating.AlreadyRated": ("This appointment has already been rated.", "تم تقييم هذا الموعد مسبقًا."),
    "DoctorRating.AppointmentNotCompleted": ("The appointment must be completed before rating.", "يجب إتمام الموعد قبل التقييم."),
    "DoctorShift.NotFound": ("Doctor shift not found.", "وردية الطبيب غير موجودة."),
    "Email.AlreadyExists": ("This email is already registered.", "البريد الإلكتروني مسجل بالفعل."),
    "Emergency.NotFound": ("Emergency case not found.", "الحالة الطارئة غير موجودة."),
    "FollowUp.AlreadyExists": ("A follow-up already exists for this appointment.", "توجد متابعة لهذا الموعد مسبقًا."),
    "FollowUp.CannotBeUpdated": ("This follow-up can no longer be updated.", "لا يمكن تعديل هذه المتابعة."),
    "FollowUp.DateNotAllowed": ("The selected date is outside the allowed follow-up window.", "التاريخ المحدد خارج فترة المتابعة المسموحة."),
    "FollowUp.NoFound": ("Follow-up not found.", "المتابعة غير موجودة."),
    "FollowUp.NotFoundOrNotPending": ("The follow-up was not found or is not pending.", "المتابعة غير موجودة أو ليست قيد الانتظار."),
    "LabRequest.AlreadyCollected": ("The samples have already been collected.", "تم سحب العينات مسبقًا."),
    "LabRequest.AlreadyPaid": ("This lab request has already been paid.", "تم دفع طلب المختبر مسبقًا."),
    "LabRequest.DuplicateItem": ("This test is already in the lab request.", "هذا الفحص موجود بالفعل في الطلب."),
    "LabRequest.NotCollected": ("The samples have not been collected yet.", "لم يتم سحب العينات بعد."),
    "LabRequest.NotFound": ("Lab request not found.", "طلب المختبر غير موجود."),
    "LabRequestItemNotFound": ("Lab request item not found.", "عنصر طلب المختبر غير موجود."),
    "LabResult.NotFound": ("Lab result not found.", "نتيجة المختبر غير موجودة."),
    "Medical.NotFound": ("Medical record not found.", "السجل الطبي غير موجود."),
    "MedicalRecord.CannotLinkToBoth": ("A record cannot be linked to both an appointment and a consultation.", "لا يمكن ربط السجل بموعد واستشارة في آن واحد."),
    "MedicalRecord.MustLinkToAppointmentOrConsultation": ("A record must be linked to an appointment or a consultation.", "يجب ربط السجل بموعد أو استشارة."),
    "Notification.NotFound": ("Notification not found.", "الإشعار غير موجود."),
    "PatientNotFound": ("Patient not found.", "المريض غير موجود."),
    "Payment.AlreadyProcessed": ("This payment has already been processed.", "تمت معالجة هذه الدفعة مسبقًا."),
    "Payment.Exists": ("A payment already exists for this item.", "توجد دفعة مسجلة مسبقًا."),
    "Payment.NotCompleted": ("The payment has not been completed.", "لم يتم إتمام الدفع."),
    "Rating.NotFound": ("Rating not found.", "التقييم غير موجود."),
    "Receptionist.NoFound": ("Receptionist not found.", "موظف الاستقبال غير موجود."),
    "Receptionist.NotFound": ("Receptionist not found.", "موظف الاستقبال غير موجود."),
    "Schedule.NotFound": ("The doctor has no schedule for this day.", "لا يوجد جدول للطبيب في هذا اليوم."),
    "ServicePrice.InvalidPrice": ("The service price is not valid.", "سعر الخدمة غير صالح."),
    "ServicePrice.NotFound": ("Service price not found.", "سعر الخدمة غير موجود."),
    "ServiceType.AlreadyExist": ("This service type already exists.", "نوع الخدمة موجود بالفعل."),
    "Shift.NotFound": ("Shift not found.", "الوردية غير موجودة."),
    "ShiftSchedule.AlreadyExists": ("This shift schedule already exists.", "جدول الوردية موجود بالفعل."),
    "ShiftSchedule.NotConfigured": ("Shift schedules have not been configured yet.", "لم يتم إعداد جداول الورديات بعد."),
    "Staff.NotFound": ("Staff member not found.", "الموظف غير موجود."),
    "StaffShift.AlreadyAssigned": ("This staff member is already assigned to a shift on that day.", "تم إسناد وردية لهذا الموظف في ذلك اليوم مسبقًا."),
    "SubPatient.NotFound": ("Sub-patient not found.", "المريض الفرعي غير موجود."),
    "SubPatient.NotFoundOrNotOwned": ("The sub-patient was not found or does not belong to you.", "المريض الفرعي غير موجود أو غير تابع لك."),
    "Subscription.AlreadyProcessed": ("This subscription has already been processed.", "تمت معالجة هذا الاشتراك مسبقًا."),
    "Subscription.NotFound": ("Subscription not found.", "الاشتراك غير موجود."),
    "Technician.NotFound": ("Lab technician not found.", "فني المختبر غير موجود."),
    "Validation.CenterIdRequired": ("A center must be selected.", "يجب تحديد المركز."),
    "Validation.InvalidTarget": ("The selected target is not valid.", "الفئة المستهدفة غير صالحة."),
    "Verify.Code": ("The verification code is invalid or expired.", "رمز التحقق غير صالح أو منتهي الصلاحية."),
    "WaitList.NotFound": ("Waitlist entry not found.", "طلب قائمة الانتظار غير موجود."),
}

TEMPLATE = '  <data name="{key}" xml:space="preserve">\n    <value>{value}</value>\n  </data>\n'


def xml_escape(text: str) -> str:
    return (text.replace("&", "&amp;")
                .replace("<", "&lt;")
                .replace(">", "&gt;"))


def patch(path: Path, index: int) -> int:
    raw = path.read_text(encoding="utf-8-sig")
    existing = set(re.findall(r'<data name="([^"]+)"', raw))

    additions = "".join(
        TEMPLATE.format(key=key, value=xml_escape(values[index]))
        for key, values in ENTRIES.items()
        if key not in existing
    )
    if not additions:
        print(f"{path.name}: nothing to add")
        return 0

    if "</root>" not in raw:
        raise SystemExit(f"{path}: no closing </root> element")

    head, _, tail = raw.rpartition("</root>")
    path.write_text(head + additions + "</root>" + tail, encoding="utf-8-sig")

    added = additions.count("<data name=")
    print(f"{path.name}: added {added} entries")
    return added


if __name__ == "__main__":
    patch(RES_DIR / "Messages.en.resx", 0)
    patch(RES_DIR / "Messages.ar.resx", 1)

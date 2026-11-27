using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sehatak.API.Security;
using Shouldly;

namespace Sehatak.IntegrationTests.Security;

/// <summary>
/// "Who can do what" - one line per endpoint, exactly as it is in the project today.
/// If somebody changes an [Authorize] (wider or narrower) this test fails and shows the line to review,
/// so a permission can never change by accident. To change on purpose: edit the line below.
/// Roles in the list are the roles allowed to call it; ANON = [AllowAnonymous]; OPEN = no attribute at all;
/// AUTH = any logged-in user.
/// </summary>
public class RoleMatrixTests
{
    private static readonly Dictionary<string, string[]> PolicyRoles = new()
    {
        ["SuperAdminOnly"] = ["SuperAdmin"],
        ["AdminOrAbove"] = ["SuperAdmin", "Admin"],
        ["AdminOnly"] = ["Admin"],
        ["MedicalStaff"] = ["Admin", "Doctor", "Receptionist", "LabTechnician", "Nurse", "GeneralDoctor"],
        ["StaffShift"] = ["Receptionist", "LabTechnician", "Nurse", "GeneralDoctor"],
        ["DoctorOnly"] = ["Doctor"],
        ["ReceptionistOnly"] = ["Receptionist"],
        ["PatientOnly"] = ["Patient"],
        ["Emergency"] = ["Admin", "Receptionist"],
        ["LabRequest"] = ["Doctor", "Receptionist", "LabTechnician"],
        ["TechnicianOnly"] = ["LabTechnician"],
    };

    private static readonly (string Name, string Roles)[] ExpectedRows =
    [
        ("AppointmentDashboardController.GetCenterAppointmentsSummary", "Admin"),   // GET appointments-summary/{centerId}
        ("AppointmentDashboardController.PatientAppointmentDashBoard", "Patient"),   // GET patient-appointment-dashboard/{centerId}
        ("AppointmentDashboardController.ReceptionistAppointmentDashBoard", "Receptionist"),   // GET receptionist-appointment-dashboard/{centerId}
        ("Appointmentcontroller.AvailableDoctorSlot", "ANON"),   // GET patient-get-available-doctor-slot/{centerId}/{doctorId}
        ("Appointmentcontroller.BookAppointment", "Patient"),   // POST patient-book/{centerId}/{doctorId}
        ("Appointmentcontroller.CancelAppointment", "Patient"),   // POST patient-cancel-appointment/{centerId}/{doctorId}
        ("Appointmentcontroller.CancelSlotAsync", "Doctor"),   // POST doctor-cancel-slot/{centerId}
        ("Appointmentcontroller.GetDoctor", "ANON"),   // GET patient-get-doctor/{centerId}/{doctorId}
        ("Appointmentcontroller.GetDoctors", "ANON"),   // GET patient-get-doctors/{centerId}
        ("Appointmentcontroller.JoinWaitLis", "Patient"),   // POST patient-join-waitlist/{centerId}/{doctorId}
        ("Appointmentcontroller.ReceptionistBookAppointment", "Receptionist"),   // POST receptionist-book-appointment/{centerId}/{doctorId}
        ("Appointmentcontroller.ReceptionistCancelAppointment", "Receptionist"),   // POST receptionist-cancel-appointment/{centerId}/{doctorId}
        ("Appointmentcontroller.ReceptionistRescheduleAppointment", "Receptionist"),   // POST receptionist-reschedule-appointment/{centerId}/{doctorId}
        ("Appointmentcontroller.RescheduleAppointment", "Patient"),   // POST patient-reschedule-appointment/{centerId}/{doctorId}
        ("Appointmentcontroller.ViewPatientsWaitList", "Receptionist"),   // GET receptionist-view-waitlist/{centerId}/{doctorId}
        ("Appointmentcontroller.ViewWaitLis", "Patient"),   // GET patient-view-my-waitlist/{centerId}/{doctorId}
        ("AuditLogController.GetAllFailedLoginAttempts", "SuperAdmin"),   // GET superAdmin-get-failed-login-attempts
        ("AuditLogController.GetFailedLoginAttempts", "Admin"),   // GET admin-get-failed-login-attempts/{centerId}
        ("AuditLogController.GetFailedStaffLoginAttempts", "Admin"),   // GET admin-get-failed-staff-login-attempts/{centerId}
        ("AuthController.LoginPatient", "ANON"),   // POST patient-login-patient/{centerId}
        ("AuthController.MedicalStaffLogin", "ANON"),   // POST staff-login/{centerId}
        ("AuthController.PatientActiveProfile", "ANON"),   // PUT patient-active-profile/{centerId}
        ("AuthController.PatientDeactiveProfile", "Patient"),   // PUT patient-deactive-profile/{centerId}
        ("AuthController.RegisterPatient", "ANON"),   // POST patient-register-patient/{centerId}
        ("AuthController.RegisterSuperAdmin", "ANON"),   // POST superAdmin-register
        ("AuthController.SuperAdminLogin", "ANON"),   // POST superAdmin_login
        ("AuthController.VerifyCode", "ANON"),   // POST patient-verify-code/{centerId}
        ("BillPaymentController.AppointmentBillPaymentAsync", "Patient"),   // GET patient-get-appointment-bill/{centerId}
        ("BillPaymentController.ConsultationBillPaymentAsync", "Patient"),   // GET patient-get-consultation-bill/{centerId}
        ("BillPaymentController.LabBillPaymentAsync", "Patient"),   // GET patient-get-lab-bill/{centerId}
        ("CenterController.ActiveCenter", "SuperAdmin"),   // PATCH superAdmin-active-center/{centerId}
        ("CenterController.CreateAdminToCenter", "SuperAdmin"),   // POST superAdmin-create-admin/{centerId}
        ("CenterController.CreateCenter", "SuperAdmin"),   // POST superAdmin-create-center
        ("CenterController.GetAllCenters", "SuperAdmin"),   // GET superAdmin-get-all-centers
        ("CenterController.GetCenterById", "SuperAdmin|Admin"),   // GET superAdmin-admin-get-center-by-Id/{centerId}
        ("CenterController.GetMyCenter", "Admin"),   // GET admin-get-my-center/{centerId}
        ("CenterController.SearchCenters", "ANON"),   // GET patient-search-centers
        ("CenterController.SuspendedCenter", "SuperAdmin"),   // PATCH superAdmin-suspened-center/{centerId}
        ("CenterRegistrationController.ApproveCenterRequest", "SuperAdmin"),   // POST superAdmin-approve-requests/{requestId}
        ("CenterRegistrationController.CenterRequest", "ANON"),   // POST center-registration
        ("CenterRegistrationController.ConfirmRegistrationPayment", "SuperAdmin"),   // POST superAdmin-confirm-payments/{paymentId}
        ("CenterRegistrationController.GetCenterRegistration", "SuperAdmin"),   // GET superAdmin-get-request/{requestId}
        ("CenterRegistrationController.GetCentersRegisteration", "SuperAdmin"),   // GET superAdmin-get-requests
        ("CenterRegistrationController.GetPendingRegistrationPayments", "SuperAdmin"),   // GET superAdmin-get-pending-payments
        ("CenterRegistrationController.RecordRegistrationPayment", "ANON"),   // POST {requestId}/record-payment
        ("CenterRegistrationController.RejectRequest", "SuperAdmin"),   // POST superAdmin-reject-requests/{requestId}
        ("ChatController.GetConversation", "Admin|Doctor|Receptionist|LabTechnician|Nurse|GeneralDoctor"),   // GET conversation/{centerId}/{otherUserId}
        ("ChatController.GetInbox", "Admin|Doctor|Receptionist|LabTechnician|Nurse|GeneralDoctor"),   // GET inbox/{centerId}
        ("ChatController.SearchUsers", "Admin|Doctor|Receptionist|LabTechnician|Nurse|GeneralDoctor"),   // GET search/{centerId}
        ("CheckTimeController.CheckInPatient", "Receptionist"),   // PUT receptionist-checkIn-patient/{centerId}/{appointmentId}
        ("CheckTimeController.FinishAppointmentTimeAsync", "Doctor"),   // PUT doctor-finish-appointment-time/{centerId}/{appointmentId}
        ("CheckTimeController.NextPatientAsync", "Receptionist"),   // PUT receptionist-next-patient/{centerId}/{appointmentId}
        ("ConfirmPaymentController.AppointmentAsync", "Receptionist"),   // POST receptionist-appointment-paid/{centerId}/{appointmentId}
        ("ConfirmPaymentController.LabPaymentAsync", "Receptionist"),   // POST receptionist-lab-paid/{centerId}/{labRequestId}
        ("ConsultationController.CancelConsultationAsync", "Patient"),   // PUT patient-cancel/{centerId}/{consultationId}
        ("ConsultationController.CompleteConsultationAsync", "Doctor"),   // PUT doctor-complete-consultation/{centerId}/{consultationId}
        ("ConsultationController.ConfirmPayment", "Doctor"),   // POST doctor-confirm-payment/{centerId}/{paymentId}
        ("ConsultationController.ConfirmRequest", "Doctor"),   // POST doctor-confirm-request/{centerId}/{consultationId}
        ("ConsultationController.ConsultationRecordPaymentAsync", "Patient"),   // POST patient-record-payment/{centerId}/{consultationId}
        ("ConsultationController.GetConsultation", "Patient"),   // GET patient-get-consultation/{centerId}/{doctorId}/{consultationId}
        ("ConsultationController.GetConsultations", "Patient"),   // GET patient-get-consultations/{centerId}
        ("ConsultationController.GetConsultationsSchedualeAsync", "Doctor"),   // GET doctor-get-consultation/{centerId}
        ("ConsultationController.GetDoctors", "ANON"),   // GET patient-get-doctors/{centerId}
        ("ConsultationController.GetPendingPayment", "Doctor"),   // GET doctor-pending-payments/{centerId}/{paymentId}
        ("ConsultationController.GetPendingPayments", "Doctor"),   // GET doctor-pending-payments/{centerId}
        ("ConsultationController.RejectConsultationPayment", "Doctor"),   // POST doctor-reject-payment/{centerId}/{paymentId}
        ("ConsultationController.RejectConsultationRequest", "Doctor"),   // POST doctor-reject-request/{centerId}/{consultationId}
        ("ConsultationController.RequestConsultation", "Patient"),   // POST patient-request/{centerId}/{doctorId}
        ("DepartmentController.AddDepartment", "SuperAdmin|Admin"),   // POST admin-add-department/{centerId}
        ("DepartmentController.AddDoctorToDepartment", "SuperAdmin|Admin"),   // POST admin-add-doctor-to-department/{centerId}
        ("DepartmentController.DeleteDepartment", "SuperAdmin|Admin"),   // DELETE admin-delete-department/{centerId}
        ("DepartmentController.EditDepartment", "SuperAdmin|Admin"),   // POST admin-edit-department/{centerId}
        ("DepartmentController.GetDepartments", "SuperAdmin|Admin"),   // GET admin-get-departments/{centerId}
        ("DoctorDailyHoursController.AddDoctorDailyHours", "Admin"),   // POST admin-add-doctor-hours/{centerId}/{doctorId}
        ("DoctorDailyHoursController.CancelDoctorDay", "Doctor"),   // PUT doctor-Cancel-doctor-day/{centerId}
        ("DoctorDailyHoursController.GetDoctorAppointmentsForDay", "Doctor"),   // GET doctor-get-appointments-for-day{centerId}
        ("DoctorDailyHoursController.GetDoctorBlockDay", "Admin"),   // GET admin-get-block-doctors-day/{centerId}
        ("DoctorDailyHoursController.GetDoctorBlockDays", "Doctor"),   // GET doctor-get-block-days{centerId}
        ("DoctorDailyHoursController.GetDoctorDailyHours", "Admin"),   // GET admin-get-doctor-hours/{centerId}/{doctorId}
        ("DoctorDailyHoursController.UpdateDoctorDailyHours", "Admin"),   // POST admin-update-doctor-hours/{centerId}/{doctorId}
        ("DoctorRatingController.AddDoctorRatingAsync", "Patient"),   // POST patient-add-doctor-rating/{centerId}
        ("DoctorRatingController.DoctorGetRatingsAsync", "Doctor"),   // GET doctor-get-ratings/{centerId}
        ("DoctorRatingController.PatientGetRatingsAsync", "Patient"),   // POST patient-get-ratings/{centerId}
        ("DoctorRatingController.RemoveDoctorRatingAsync", "Patient"),   // POST patient-remove-doctor-rating/{centerId}/{ratingId}
        ("DoctorRatingController.UpdateDoctorRatingAsync", "Patient"),   // POST patient-update-doctor-rating/{centerId}
        ("EditPatientProfileController.ConfirmEditPatientEmail", "Patient"),   // POST patient-confirm-edit-patient-email/{centerId}
        ("EditPatientProfileController.ConfirmEditPatientPassword", "Patient"),   // POST patient-confirm-edit-patient-password/{centerId}
        ("EditPatientProfileController.ConfirmForgetPassword", "ANON"),   // POST confirm-forget-password/{centerId}
        ("EditPatientProfileController.EditPatientEmail", "Patient"),   // POST patient-edit-patient-email/{centerId}
        ("EditPatientProfileController.EditPatientInformation", "Patient"),   // PUT patient-edit-patient-information/{centerId}
        ("EditPatientProfileController.EditPatientPassword", "Patient"),   // POST patient-edit-patient-password/{centerId}
        ("EditPatientProfileController.ForgetPassword", "ANON"),   // POST forget-password/{centerId}
        ("EditPatientProfileController.ViewPatientInformation", "Patient"),   // GET patient-view-patient-information/{centerId}
        ("EditStaffProfileController.ConfirmEmail", "Admin|Doctor|Receptionist|LabTechnician|Nurse|GeneralDoctor"),   // POST MedicalStaff-edit-staff-confirm-email-code/{centerId}
        ("EditStaffProfileController.ConfirmPassword", "Admin|Doctor|Receptionist|LabTechnician|Nurse|GeneralDoctor"),   // POST MedicalStaff-edit-staff-confirm-password-code/{centerId}
        ("EditStaffProfileController.EditStaffEmail", "Admin|Doctor|Receptionist|LabTechnician|Nurse|GeneralDoctor"),   // POST MedicalStaff-edit-staff-email/{centerId}
        ("EditStaffProfileController.EditStaffInformation", "Admin|Doctor|Receptionist|LabTechnician|Nurse|GeneralDoctor"),   // PUT MedicalStaff-edit-staff-information/{centerId}
        ("EditStaffProfileController.EditStaffPasswored", "Admin|Doctor|Receptionist|LabTechnician|Nurse|GeneralDoctor"),   // POST MedicalStaff-edit-staff-password/{centerId}
        ("EditSuperAdminProfileController.ConfirmEditEmail", "SuperAdmin"),   // POST superAdmin--confirm-edit-email/{superAdminId}
        ("EditSuperAdminProfileController.ConfirmEditPassword", "SuperAdmin"),   // POST superAdmin--confirm-edit-password/{superAdminId}
        ("EditSuperAdminProfileController.EditName", "SuperAdmin"),   // PUT superAdmin--edit-name/{superAdminId}
        ("EditSuperAdminProfileController.EditProfileImage", "SuperAdmin"),   // PUT superAdmin--edit-profile-image/{superAdminId}
        ("EditSuperAdminProfileController.RequestEditEmail", "SuperAdmin"),   // POST superAdmin--edit-email/{superAdminId}
        ("EditSuperAdminProfileController.RequestEditPassword", "SuperAdmin"),   // POST superAdmin--edit-password/{superAdminId}
        ("EditSuperAdminProfileController.superAdminViewProfile", "SuperAdmin"),   // GET superAdmin--view-profile/{superAdminId}
        ("EmergencyController.GetGeneralDoctoryAsync", "Receptionist"),   // GET receptionist-get-general-doctor/{centerId}
        ("EmergencyController.GetPatientEmergencyAsync", "Admin|Receptionist"),   // GET receptionist-get-patient-emergency/{centerId}
        ("EmergencyController.RegisterPatientEmergenceAsync", "Receptionist"),   // POST receptionist-add-patient-emergency/{centerId}
        ("EmergencyController.UpdatePatientEmergencyAsync", "Receptionist"),   // POST receptionist-update-patient-emergency/{centerId}
        ("ExpenseController.AddExpense", "Admin"),   // POST admin-add-expense/{centerId}
        ("ExpenseController.DeleteExpense", "Admin"),   // DELETE admin-delete-expense/{centerId}/{expenseId}
        ("ExpenseController.GetExpenses", "Admin"),   // GET admin-get-expenses/{centerId}
        ("ExpenseController.UpdateExpense", "Admin"),   // PUT admin-update-expense/{centerId}/{expenseId}
        ("FeactureController.ActiveFeature", "SuperAdmin"),   // POST superAdmin-active-feature/{centerId}
        ("FeactureController.AddFeatureToCenter", "SuperAdmin"),   // POST add-feature-to-center/{centerId}
        ("FeactureController.GetAllFeature", "SuperAdmin|Admin"),   // GET superAdminOrAdmin-get-all-feature
        ("FeactureController.RemoveFeatureFromCenter", "SuperAdmin"),   // DELETE superAdmin-remove-feature-from-center/{centerId}
        ("FeedbackController.GetFeedbacks", "SuperAdmin"),   // GET superAdmin-get-feedbacks
        ("FeedbackController.GetMyFeedbacks", "AUTH"),   // GET get-my-feedbacks/{centerId}
        ("FeedbackController.GetSuperAdminNotifications", "SuperAdmin"),   // GET superAdmin-get-notifications
        ("FeedbackController.GetUnreadNotificationsCount", "SuperAdmin"),   // GET superAdmin-unread-notifications-count
        ("FeedbackController.MarkAllNotificationsRead", "SuperAdmin"),   // PUT superAdmin-read-all-notifications
        ("FeedbackController.MarkNotificationRead", "SuperAdmin"),   // PUT superAdmin-read-notification/{notificationId}
        ("FeedbackController.ReplyToFeedback", "SuperAdmin"),   // POST superAdmin-reply-feedback/{feedbackId}
        ("FeedbackController.SubmitFeedback", "AUTH"),   // POST submit-feedback/{centerId}
        ("FeedbackController.SubmitGuestFeedback", "ANON"),   // POST submit-guest-feedback
        ("FeedbackController.UpdateFeedbackStatus", "SuperAdmin"),   // PATCH superAdmin-update-feedback-status/{feedbackId}
        ("FinancialReportController.ExportReport", "SuperAdmin"),   // GET superAdmin-financial-report
        ("FinancialReportController.FinancialReport", "Admin"),   // GET admin/{centerId}
        ("FinancialReportController.NetProfit", "Admin"),   // GET admin-net-profit/{centerId}
        ("FollowUpController.DoctorAddFollowUpAsync", "Doctor"),   // POST doctor-add-followup/{centerId}
        ("FollowUpController.DoctorGetAllFollowUpAsync", "Doctor"),   // GET doctor-get-followups/{centerId}
        ("FollowUpController.DoctorUpdateFollowUpAsync", "Doctor"),   // POST doctor-update-followup/{centerId}
        ("FollowUpController.PatientGetAllFollowUpAsync", "Patient"),   // GET patient-get-followups/{centerId}
        ("FollowUpController.ReceptionistAddFollowUpAsync", "Receptionist"),   // POST receptionist-add-followup/{centerId}
        ("FollowUpController.ReceptionistGetAllFollowUpAsync", "Receptionist"),   // GET receptionist-get-followups/{centerId}
        ("FollowUpController.ReceptionistUpdateFollowUpAsync", "Receptionist"),   // POST receptionist-update-followup/{centerId}
        ("GetPatientCenterController.GetPatientAsync", "SuperAdmin|Admin"),   // GET admin-get-patient-from-center/{centerId}
        ("GetPatientCenterController.GetPatientsAsync", "SuperAdmin|Admin"),   // GET admin-get-patients-from-center/{centerId}
        ("GetSttafController.GetDoctor", "Admin"),   // GET admin-get-doctor/{centerId}/{doctorId}
        ("GetSttafController.GetDoctorsWithDepartments", "Admin"),   // GET admin-get-doctors/{centerId}
        ("LabController.CancleLabRequestsAsync", "Doctor|Receptionist|LabTechnician"),   // PUT receptionist-or-doctor-cancle-lab-requests/{centerId}/{labRequestId}
        ("LabController.CreateLabRequest", "Doctor"),   // POST doctor-create-lab-request/{centerId}
        ("LabController.DoctorGetLabRequestsAsync", "Doctor"),   // GET doctor-get-patient-lab-requests/{centerId}
        ("LabController.DoctorGetLabResultAsync", "Doctor"),   // GET doctor-get-patient-lab-result/{centerId}
        ("LabController.PatientGetLabRequestsAsync", "Patient"),   // GET patient-get-lab-requests/{centerId}
        ("LabController.PatientGetLabResultsAsync", "Patient"),   // GET patient-get-lab-results/{centerId}
        ("LabController.ReceptionistCreateLabRequestsAsync", "Receptionist"),   // POST receptionist-create-patient-lab-requests/{centerId}
        ("LabController.ReceptionistGetLabRequestsAwaitingPaymentAsync", "Receptionist"),   // GET receptionist-get-lab-requests-await-payment/{centerId}
        ("LabController.ReceptionistUpdateLabRequestsAsync", "Receptionist"),   // PUT receptionist-update-lab-requests/{centerId}
        ("LabController.TechnicianCollectedLabRequestAsync", "LabTechnician"),   // PUT technician-collected-lab-request/{centerId}/{labRequestId}
        ("LabController.TechnicianGetLabRequestAsync", "LabTechnician"),   // GET technician-get-lab-request/{centerId}/{labRequestId}
        ("LabController.TechnicianGetLabRequestsAsync", "LabTechnician"),   // GET technician-get-lab-pending-requests/{centerId}
        ("LabController.TechnicianUplodLabRequestAsync", "LabTechnician"),   // PUT technician-uplod-lab-request/{centerId}
        ("LabController.UpdateLabRequest", "Doctor"),   // PUT doctor-update-lab-request/{centerId}
        ("MedicalRecordController.AddMedicakRecor", "Doctor"),   // POST doctor-add-medicalrecord/{centerId}
        ("MedicalRecordController.GetMedicalRecordById", "Doctor"),   // GET doctor-record/{centerId}/{medicalRecordId}
        ("MedicalRecordController.GetPatientMedicalHistory", "Doctor"),   // GET doctor-patient-history/{centerId}/{patientId}
        ("MedicalRecordController.GetPatientMedicalHistory", "Patient"),   // GET patient-get-medical-history/{centerId}
        ("MedicalRecordController.UpdateMedicakRecor", "Doctor"),   // POST doctor-update-medicalrecord/{centerId}
        ("MedicationReminderController.AddMedicationReminderAsync", "Patient"),   // POST patient-add-medication-reminder/{centerId}
        ("MedicationReminderController.DeleteMedicationReminderAsync", "Patient"),   // GET delete-medication-reminder/{centerId}
        ("MedicationReminderController.GetMedicationReminderAsync", "Patient"),   // GET get-medications-reminders/{centerId}
        ("MigrateTenantController.MigrateAllTenants", "SuperAdmin"),   // POST superAdmin-migrate-all-tenants
        ("MigrateTenantController.MigrateSingleTenant", "SuperAdmin"),   // POST superAdmin-migrate-tenant/{centerId}
        ("NotificationController.GetNotifications", "AUTH"),   // GET get-notifications/{centerId}
        ("NotificationController.GetNotifications", "AUTH"),   // PUT read-notifications/{centerId}/{notificationId}
        ("NotificationController.RegisterDeviceToken", "AUTH"),   // PUT register-device-token/{centerId}
        ("PlanController.AssignFeature", "SuperAdmin"),   // POST superAdmin-assign-feature/{planId}
        ("PlanController.EditPlan", "SuperAdmin"),   // POST superAdmin-edit-plan/{planId}
        ("PlanController.GetFeatures", "SuperAdmin|Admin"),   // GET superAdminOrAdmin-get-features/{planId}
        ("PlanController.ListOfPlan", "SuperAdmin|Admin"),   // GET list-of-plan
        ("PlanController.SupscriptionPlan", "SuperAdmin"),   // POST superAdmin-supscription-plan
        ("RenewSubscriptionController.CancleSubscription", "Admin"),   // POST cancle-subscription/{centerId}
        ("RenewSubscriptionController.RenewActiveSubscription", "SuperAdmin|Admin"),   // POST renew-active-subscription/{centerId}
        ("RenewSubscriptionController.RenewExpiredSubscription", "SuperAdmin|Admin"),   // POST renew-expird-subscription/{centerId}
        ("SalaryController.GetSalaries", "Admin"),   // GET admin-get-salaries/{centerId}
        ("SalaryController.SetCategorySalary", "Admin"),   // PUT admin-set-category-salary/{centerId}
        ("SalaryController.SetDoctorSalary", "Admin"),   // PUT admin-set-doctor-salary/{centerId}
        ("SearchDoctorControlle.SearchDoctor", "ANON"),   // GET search-doctor-name/{centerId}
        ("SendEmailsController.SendEmail", "SuperAdmin"),   // POST send-email
        ("ServicePriceController.AddSrevicePrice", "Admin"),   // POST admin-add-service-price/{centerId}
        ("ServicePriceController.GetServicePriceByCenterId", "Admin|Doctor|Receptionist|LabTechnician|Nurse|GeneralDoctor"),   // GET medicalStaff-get-service-price/{centerId}
        ("ServicePriceController.RemoveSrevicePrice", "Admin"),   // PUT admin-remove-service-price/{centerId}/{servicePriceId}
        ("ServicePriceController.UpdateSrevicePrice", "Admin"),   // PUT admin-update-service-price/{centerId}
        ("ShiftController.AddShifSchedule", "Admin"),   // POST admin-add-shif-schedule/{centerId}
        ("ShiftController.AddStaffShift", "Admin"),   // POST admin-add-staff-shift/{centerId}
        ("ShiftController.DeleteShift", "Admin"),   // DELETE admin-delete-shift-schedule/{centerId}/{shiftId}
        ("ShiftController.GetAllSTaffs", "Admin"),   // GET admin-get-all-staffs/{centerId}
        ("ShiftController.GetShifts", "Admin"),   // GET admin-get-shifts/{centerId}
        ("ShiftController.GetStaffsWithShift", "Admin"),   // GET admin-get-staffs-schedule/{centerId}
        ("ShiftController.UpdateShift", "Admin"),   // POST admin-update-shift-schedule/{centerId}
        ("StaffAttendanceController.ChickInTimeAsync", "Receptionist|LabTechnician|Nurse|GeneralDoctor"),   // POST StaffShift-chickin-time/{centerId}
        ("StaffAttendanceController.ChickOutTimeAsync", "Receptionist|LabTechnician|Nurse|GeneralDoctor"),   // POST StaffShift-chickout-time/{centerId}
        ("StaffAttendanceController.OnLeaveAsync", "Receptionist|LabTechnician|Nurse|GeneralDoctor"),   // POST StaffShift-onleave/{centerId}
        ("StaffAttendanceController.StaffAbsentAsync", "Admin"),   // POST admin-absent/{centerId}
        ("StaffController.ActiveStaff", "SuperAdmin|Admin"),   // PUT admin-active-staff-from-center/{centerId}
        ("StaffController.AddSttaf", "Admin"),   // POST admin-add-staff/{centerId}
        ("SubPatientController.AddSubPatientAsync", "Patient"),   // POST patient-add-sub-patient/{centerId}
        ("SubPatientController.GetSubPatientsAsync", "Patient"),   // GET patient-get-sub-patient/{centerId}
        ("SubPatientController.UpdateSubPatientAsync", "Patient"),   // PUT patient-update-sub-patient/{centerId}/{subPatientId}
        ("SubscriptionPaymentController.ConfirmPayment", "SuperAdmin"),   // POST superAdmin-confirm-payment/{paymentId}
        ("SubscriptionPaymentController.GetCenterPayment", "SuperAdmin"),   // GET superAdmin-center-payment/{centerId}
        ("SubscriptionPaymentController.GetPendingPayment", "SuperAdmin"),   // GET superAdmin-pending-payment
        ("SubscriptionPaymentController.RecordPayment", "SuperAdmin|Admin"),   // POST admin-record-payment/{centerId}
        ("TokenController.Logout", "ANON"),   // POST logout
        ("TokenController.Refresh", "ANON"),   // POST refresh
        ("ViewProfileController.DoctorViewProfileAsync", "Doctor"),   // GET doctor-view-profile/{centerId}
        ("ViewProfileController.PatientViewProfile", "Patient"),   // GET patient-view-profile/{centerId}
        ("ViewProfileController.StaffViewProfileAsync", "Admin|Doctor|Receptionist|LabTechnician|Nurse|GeneralDoctor"),
        ("FeactureController.AddFeature", "SuperAdmin"),
        ("MedicationReminderController.UpdateMedicationReminderAsync", "Patient"),
        ("StaffController.RemoveStaff", "Admin|SuperAdmin"),
        ("RenewSubscriptionController.GetMySubscription", "Admin|SuperAdmin"),
        ("SubscriptionPaymentController.GetMyCenterPayments", "Admin"),
        ("EditStaffProfileController.EditCenterInformation", "Admin"),// GET staff-view-profile/{centerId}
        ("Appointmentcontroller.ReceptionistEnterFromWaitlist", "Receptionist"),   // POST receptionist-enter-from-waitlist/{centerId}/{doctorId}
        ("Appointmentcontroller.ReceptionistRemoveFromWaitlist", "Receptionist"),   // DELETE receptionist-remove-from-waitlist/{centerId}/{doctorId}/{waitListId}
    ];

    private static readonly ILookup<string, string> Expected = ExpectedRows.ToLookup(r => r.Name, r => Canon(r.Roles));

    private static IEnumerable<(string Name, MethodInfo Method, Type Controller)> Actions() =>
        typeof(CenterAccessFilter).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes().OfType<IActionHttpMethodProvider>().Any())
                .Select(m => ($"{t.Name}.{m.Name}", m, t)));

    /// <summary>Same rule as ASP.NET: every [Authorize] must pass (AND); inside one attribute the roles are OR.</summary>
    internal static string Effective(Type controller, MethodInfo method)
    {
        if (method.GetCustomAttribute<AllowAnonymousAttribute>(true) != null ||
            controller.GetCustomAttribute<AllowAnonymousAttribute>(true) != null) return "ANON";

        var attrs = controller.GetCustomAttributes<AuthorizeAttribute>(true)
            .Concat(method.GetCustomAttributes<AuthorizeAttribute>(true)).ToList();
        if (attrs.Count == 0) return "OPEN";

        IEnumerable<string>? allowed = null;
        foreach (var a in attrs)
        {
            IEnumerable<string>? set = null;
            if (!string.IsNullOrEmpty(a.Policy))
                set = PolicyRoles.TryGetValue(a.Policy, out var r) ? r : [];
            if (!string.IsNullOrEmpty(a.Roles))
            {
                var byRole = a.Roles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                set = set == null ? byRole : set.Intersect(byRole);
            }
            if (set != null) allowed = allowed == null ? set : allowed.Intersect(set);
        }
        if (allowed == null) return "AUTH";
        var list = allowed.ToList();
        return list.Count == 0 ? "NOBODY" : string.Join("|", list.OrderBy(x => x, StringComparer.Ordinal));
    }

    internal static string Canon(string s) => s is "ANON" or "ANON" or "AUTH" or "NOBODY"
        ? s : string.Join("|", s.Split('|').OrderBy(x => x, StringComparer.Ordinal));

    [Fact]
    public void Every_endpoint_has_exactly_the_permissions_listed_in_the_matrix()
    {
        // actions with the same method name (overloads) are compared as a sorted list
        var actual = Actions()
            .Where(a => a.Name != "AuditLogController.GetAuditLogs")   // has its own test below
            .GroupBy(a => a.Name)
            .ToDictionary(g => g.Key, g => g.Select(a => Canon(Effective(a.Controller, a.Method))).OrderBy(x => x, StringComparer.Ordinal).ToList());

        var diffs = new List<string>();
        foreach (var (name, roles) in actual)
        {
            var exp = Expected[name].OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (exp.Count == 0) diffs.Add($"NEW endpoint, not in the matrix: {name} = {string.Join(" / ", roles)}");
            else if (!exp.SequenceEqual(roles)) diffs.Add($"{name}: matrix says {string.Join(" / ", exp)} but the code says {string.Join(" / ", roles)}");
        }
        foreach (var k in Expected.Select(g => g.Key).Where(k => !actual.ContainsKey(k))) diffs.Add($"in the matrix but no longer in the code: {k}");

        diffs.ShouldBeEmpty(string.Join(Environment.NewLine, diffs));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public void The_audit_log_endpoint_is_for_the_center_admin()
    {
        // [Authorize(Roles = "AdminOnly")] names a POLICY as if it were a ROLE: no user has a role called AdminOnly,
        // so nobody (not even the admin) can open the audit log. It must be [Authorize(Policy = "AdminOnly")].
        var (_, m, c) = Actions().Single(a => a.Name == "AuditLogController.GetAuditLogs");
        Effective(c, m).ShouldBe("Admin");
    }

    [Fact]
    public void Roles_written_directly_in_an_Authorize_attribute_are_real_roles()
    {
        var known = new HashSet<string> { "SuperAdmin", "Admin", "Doctor", "Receptionist", "LabTechnician", "Patient", "Nurse", "GeneralDoctor" };
        var bad = Actions()
            .SelectMany(a => a.Controller.GetCustomAttributes<AuthorizeAttribute>(true).Concat(a.Method.GetCustomAttributes<AuthorizeAttribute>(true))
                .Where(x => !string.IsNullOrEmpty(x.Roles))
                .SelectMany(x => x.Roles!.Split(',', StringSplitOptions.TrimEntries))
                .Where(r => !known.Contains(r)).Select(r => $"{a.Name}: '{r}'"))
            .Distinct().ToList();

        bad.ShouldBeEmpty("a policy name used as a role locks everybody out: " + string.Join(", ", bad));
    }


    [Fact]
    public void A_patient_is_never_allowed_on_a_staff_or_admin_endpoint()
    {
        var leaks = Actions()
            .Select(a => (a.Name, Roles: Effective(a.Controller, a.Method)))
            .Where(a => a.Roles.Split('|').Contains("Patient") && a.Roles != "Patient")
            .ToList();

        leaks.ShouldBeEmpty(string.Join(", ", leaks.Select(l => $"{l.Name}={l.Roles}")));
    }
}

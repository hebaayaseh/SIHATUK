using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Entities;
using Sehatak.Domain.Entities.General;
using Sehatak.Domain.Entities.TenantEntities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Data
{
    public class TenantDbContext : DbContext
    {
        public TenantDbContext(DbContextOptions<TenantDbContext> options)
        : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<Doctorschedule> DoctorSchedules => Set<Doctorschedule>();
        public DbSet<StaffShift> StaffShifts => Set<StaffShift>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<Waitlist> Waitlists => Set<Waitlist>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
        public DbSet<LabResult> LabResults => Set<LabResult>();
        public DbSet<Consultation> Consultations => Set<Consultation>();
        public DbSet<DoctorRating> DoctorRatings => Set<DoctorRating>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<Chat> Chats => Set<Chat>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<AppointmentItem> AppointmentItems => Set<AppointmentItem>();
        public DbSet<FollowUp> FollowUps => Set<FollowUp>();
        public DbSet<PostponedService> PostponedServices => Set<PostponedService>();
        public DbSet<StaffAttendance> StaffAttendances => Set<StaffAttendance>();
        public DbSet<LabRequest> LabRequests => Set<LabRequest>();
        public DbSet<LabRequestItem> LabRequestItems => Set<LabRequestItem>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        public DbSet<EmailVerificationCode> EmailVerificationCodes => Set<EmailVerificationCode>();
        public DbSet<DoctorBlockedDay> DoctorBlockedDays => Set<DoctorBlockedDay>();
        public DbSet<ShiftSchedule> shiftSchedules => Set<ShiftSchedule>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            //  USER 
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("users");
                entity.HasKey(e => e.Id);

                entity.HasIndex(e => e.email)
                      .IsUnique()
                      .HasFilter("email IS NOT NULL");

                entity.Property(e => e.firstName)
                      .IsRequired()
                      .HasMaxLength(100);

                entity.Property(e => e.lastName)
                      .IsRequired()
                      .HasMaxLength(100);

                entity.Property(e => e.passwordHash)
                      .IsRequired()
                      .HasMaxLength(256);

                entity.HasIndex(e => e.phoneNumber).IsUnique();
                entity.Property(entity => entity.phoneNumber)
                      .HasMaxLength(20);

                entity.Property(entity => entity.address)
                      .HasMaxLength(500);

                entity.Property(e => e.ProfileImageUrl)
                        .HasMaxLength(500);

                entity.Property(e => e.role)
                      .HasConversion<string>();
            });

            //  DEPARTMENT 
            modelBuilder.Entity<Department>(entity =>
            {
                entity.ToTable("departments");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                      .IsRequired()
                      .HasMaxLength(100);
            });

            //  DOCTOR 
            modelBuilder.Entity<Doctor>(entity =>
            {
                entity.ToTable("doctors");
                entity.HasKey(e => e.Id);


                // كل يوزر يكون دكتور وحد بس
                entity.HasIndex(e => e.userId).IsUnique();


                entity.Property(e => e.Specialization)
                .HasMaxLength(256);

                // علاقة مع User
                entity.HasOne(e => e.user)
                      .WithOne(u => u.doctor)
                      .HasForeignKey<Doctor>(e => e.userId)
                      .IsRequired(false)
                      .OnDelete(DeleteBehavior.Cascade);

                // علاقة مع Department
                entity.HasOne(e => e.department)
                      .WithMany(d => d.Doctors)
                      .HasForeignKey(e => e.departmentId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.ConsultationCost)
                      .WithMany()
                      .HasForeignKey(e => e.ConsultationCostId)
                      .OnDelete(DeleteBehavior.SetNull);

            });

            //  PATIENT 
            modelBuilder.Entity<Patient>(entity =>
            {
                entity.ToTable("patients");
                entity.HasKey(e => e.patientId);

                entity.Property(e => e.Gender)
                      .HasConversion<string>();

                // علاقة مع User
                entity.HasOne(e => e.user)
                      .WithOne(u => u.patient)
                      .HasForeignKey<Patient>(e => e.userId)
                      .OnDelete(DeleteBehavior.SetNull);

                // علاقة المريض الفرعي مع الرئيسي
                entity.HasOne(e => e.ParentPatient)
                      .WithMany(p => p.SubPatients)
                      .HasForeignKey(e => e.SubPatientId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.Property(e => e.SubPatientName).HasMaxLength(200);

                entity.Property(e => e.BloodType)
                      .HasConversion<string>();
            });

            // DOCTOR SCHEDULE 
            modelBuilder.Entity<Doctorschedule>(entity =>
            {
                entity.ToTable("doctor_schedules");
                entity.HasKey(e => e.Id);

                // DayOfWeek ينحفظ كـ string بالداتا بيس
                entity.Property(e => e.DayOfWeek)
                      .HasConversion<string>();

                entity.HasOne(e => e.doctor)
                      .WithMany(d => d.doctorschedules)
                      .HasForeignKey(e => e.DoctorId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            //  STAFF SHIFT 
            modelBuilder.Entity<StaffShift>(entity =>
            {
                entity.ToTable("staff_shifts");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.ShiftDate)
                      .HasConversion<string>();

                entity.Property(e => e.ShiftName)
                      .HasMaxLength(100);

                entity.HasOne(e => e.Staff)
                      .WithMany(u => u.Shifts)
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            //  APPOINTMENT 
            modelBuilder.Entity<Appointment>(entity =>
            {
                entity.ToTable("appointments");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.prepayment)
                      .HasPrecision(10, 2);

                entity.Property(e => e.BillAmount)
                      .HasPrecision(10, 2);

                entity.Property(e => e.appointmentStatus)
                      .HasConversion<string>();

                // علاقة مع Patient
                entity.HasOne(e => e.Patient)
                      .WithMany(p => p.appointments)
                      .HasForeignKey(e => e.patientId)
                      .OnDelete(DeleteBehavior.Restrict);

                // علاقة مع Doctor
                entity.HasOne(e => e.Doctor)
                      .WithMany(d => d.dailyAppointment)
                      .HasForeignKey(e => e.doctorId)
                      .OnDelete(DeleteBehavior.Restrict);

                // علاقة مع Receptionist — NoAction عشان ما يحذف الموعد لو الريسبشن اتحذف
                entity.HasOne(e => e.Receptionist)
                      .WithMany()
                      .HasForeignKey(e => e.ReceptionistId)
                      .OnDelete(DeleteBehavior.NoAction);
            });
            // servicePrice 
            modelBuilder.Entity<ServicePrice>(entity =>
            {
                entity.ToTable("service_prices");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ServiceName).IsRequired().HasMaxLength(256);
                entity.Property(e => e.Price).HasPrecision(10, 2);
                entity.Property(e => e.Type).HasConversion<string>();

                entity.HasIndex(e => e.Type);
            });
            // APPOINTMENT ITEM
            modelBuilder.Entity<AppointmentItem>(entity =>
            {
                entity.ToTable("appointment_items");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.UnitPrice).HasPrecision(10, 2);
                entity.Property(e => e.TotalPrice).HasPrecision(10, 2);

                entity.HasOne(e => e.Appointment)
                      .WithMany(a => a.Items)
                      .HasForeignKey(e => e.AppointmentId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.ServicePrice)
                      .WithMany()
                      .HasForeignKey(e => e.ServicePriceId)
                      .OnDelete(DeleteBehavior.Restrict);
            });


            //  WAITLIST
            modelBuilder.Entity<Waitlist>(entity =>
            {
                entity.ToTable("waitlists");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Status)
                      .HasConversion<string>();

                entity.HasOne(e => e.Patient)
                      .WithMany(p => p.Waitlists)
                      .HasForeignKey(e => e.PatientId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Doctor)
                      .WithMany(d => d.Waitlists)
                      .HasForeignKey(e => e.DoctorId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Receptionist)
                      .WithMany()
                      .HasForeignKey(e => e.ReceptionistId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            //  PAYMENT 
            modelBuilder.Entity<Payment>(entity =>
            {
                entity.ToTable("payments");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Amount).HasPrecision(10, 2);
                entity.Property(e => e.Method).HasConversion<string>();
                entity.Property(e => e.Type).HasConversion<string>();
                entity.Property(e => e.Status).HasConversion<string>();

                // Appointment — One-to-One
                entity.HasOne(e => e.Appointment)
                      .WithOne(a => a.Payment)
                      .HasForeignKey<Payment>(e => e.AppointmentId)
                      .OnDelete(DeleteBehavior.SetNull);

                // LabResult — One-to-One
                entity.HasOne(e => e.LabResult)
                      .WithOne(l => l.Payment)
                      .HasForeignKey<Payment>(e => e.LabResultId)
                      .OnDelete(DeleteBehavior.SetNull);

                // Consultation — One-to-One
                entity.HasOne(e => e.Consultation)
                      .WithOne(c => c.Payment)
                      .HasForeignKey<Payment>(e => e.ConsultationId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.Patient)
                      .WithMany(p => p.payments)
                      .HasForeignKey(e => e.PatientId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Staff)
                      .WithMany()
                      .HasForeignKey(e => e.RecordedByStaffId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            // MEDICAL RECORD 
            modelBuilder.Entity<MedicalRecord>(entity =>
            {
                entity.ToTable("medical_records");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Notes)
                .HasMaxLength(500);

                entity.Property(e => e.Prescription)
                .HasMaxLength(500);

                entity.HasOne(e => e.Patient)
                      .WithMany(p => p.MedicalRecords)
                      .HasForeignKey(e => e.PatientId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Doctor)
                      .WithMany(d => d.medicicalRecord)
                      .HasForeignKey(e => e.DoctorId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Appointment)
                      .WithOne(a => a.MedicalRecord)
                      .HasForeignKey<MedicalRecord>(e => e.AppointmentId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            //  LAB RESULT 
            modelBuilder.Entity<LabResult>(entity =>
            {
                entity.ToTable("lab_results");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Status).HasConversion<string>();

                entity.HasIndex(e => new { e.PatientId, e.Status }); // بحث سريع

                entity.HasOne(e => e.Payment)
                      .WithOne(p => p.LabResult)
                      .HasForeignKey<LabResult>(e => e.PaymentId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.Patient)
                      .WithMany(p => p.LabResults)
                      .HasForeignKey(e => e.PatientId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Technician)
                      .WithMany()
                      .HasForeignKey(e => e.TechnicianId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.LabRequest)
                      .WithOne(r => r.LabResult)
                      .HasForeignKey<LabResult>(e => e.LabRequestId)
                      .OnDelete(DeleteBehavior.SetNull);
            });
            // FOLLOW UP
            modelBuilder.Entity<FollowUp>(entity =>
            {
                entity.ToTable("follow_ups");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Status).HasConversion<string>();

                entity.HasOne(e => e.OriginalAppointment)
                      .WithMany()
                      .HasForeignKey(e => e.OriginalAppointmentId)
                      .OnDelete(DeleteBehavior.Restrict);


                entity.HasOne(e => e.Patient)
                      .WithMany()
                      .HasForeignKey(e => e.PatientId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Doctor)
                      .WithMany()
                      .HasForeignKey(e => e.DoctorId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Receptionist)
                      .WithMany()
                      .HasForeignKey(e => e.ReceptionistId)
                      .OnDelete(DeleteBehavior.Restrict);
            });
            // AUDIT LOG
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("audit_logs");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Restrict); 

                entity.HasIndex(e => new { e.EntityType, e.EntityId }); 
                entity.HasIndex(e => e.CreatedAt);
            });

            // POSTPONED SERVICE
            modelBuilder.Entity<PostponedService>(entity =>
            {
                entity.ToTable("postponed_services");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Type).HasConversion<string>();
                entity.Property(e => e.Status).HasConversion<string>();

                entity.HasOne(e => e.Patient)
                      .WithMany()
                      .HasForeignKey(e => e.PatientId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.CreatedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.CreatedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Appointment)
                      .WithMany()
                      .HasForeignKey(e => e.AppointmentId)
                      .OnDelete(DeleteBehavior.SetNull);


                entity.HasOne(e => e.Consultation)
                      .WithMany()
                      .HasForeignKey(e => e.ConsultationId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // CONSULTATION 
            modelBuilder.Entity<Consultation>(entity =>
            {
                entity.ToTable("consultations");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Status)
                      .HasConversion<string>();

                entity.HasOne(e => e.Patient)
                      .WithMany(p => p.Consultations)
                      .HasForeignKey(e => e.PatientId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Doctor)
                      .WithMany(d => d.Consultations)
                      .HasForeignKey(e => e.DoctorId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            //  DOCTOR RATING 
            modelBuilder.Entity<DoctorRating>(entity =>
            {
                entity.ToTable("doctor_ratings");
                entity.HasKey(e => e.Id);

                // كل موعد ما ينقيّم أكثر من مرة
                entity.HasIndex(e => e.AppointmentId).IsUnique();

                // التقييم بين 1 و 5
                entity.Property(e => e.Rating)
                      .HasAnnotation("Range", new[] { 1, 5 });

                entity.HasOne(e => e.Doctor)
                      .WithMany(d => d.doctorRatings)
                      .HasForeignKey(e => e.DoctorId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Patient)
                      .WithMany(p => p.Ratings)
                      .HasForeignKey(e => e.PatientId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Appointment)
                      .WithOne(a => a.Rating)
                      .HasForeignKey<DoctorRating>(e => e.AppointmentId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            //StaffAttendance
            modelBuilder.Entity<StaffAttendance>(entity =>
            {
                entity.ToTable("staff_attendance");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.attendanceStatus)
                      .HasConversion<string>();

                entity.HasIndex(e => new { e.UserId, e.AttendanceDate }).IsUnique();

                entity.HasOne(e => e.Staff)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Shift)
                      .WithMany()
                      .HasForeignKey(e => e.StaffShiftId)
                      .OnDelete(DeleteBehavior.Restrict);

            });

            modelBuilder.Entity<ShiftSchedule>(entity =>
            {
                entity.ToTable("staff_schedule");
                entity.HasKey(e => e.Id);
            });


            //  NOTIFICATION 
            modelBuilder.Entity<Notification>(entity =>
            {
                entity.ToTable("notifications");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Type)
                      .HasConversion<string>();

                entity.HasOne(e => e.User)
                      .WithMany(u => u.notification)
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // CHAT 
            modelBuilder.Entity<Chat>(entity =>
            {
                entity.ToTable("chats");
                entity.HasKey(e => e.Id);

                // علاقة المرسل — NoAction عشان ما يحذف الرسائل لو المرسل اتحذف
                entity.HasOne(e => e.Sender)
                      .WithMany(u => u.sentMessages)
                      .HasForeignKey(e => e.SenderId)
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(e => e.Receiver)
                      .WithMany(u => u.receivedMessages)
                      .HasForeignKey(e => e.ReceiverId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            // Lap Request 
            modelBuilder.Entity<LabRequest>(entity =>
            {
                entity.ToTable("lab_requests");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Status).HasConversion<string>();

                entity.HasIndex(e => new { e.PatientId, e.Status });

                entity.HasOne(e => e.Doctor)
                      .WithMany()
                      .HasForeignKey(e => e.DoctorId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Patient)
                      .WithMany()
                      .HasForeignKey(e => e.PatientId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Appointment)
                      .WithMany()
                      .HasForeignKey(e => e.AppointmentId)
                      .OnDelete(DeleteBehavior.SetNull);
            });
            // Lab Request Item
            modelBuilder.Entity<LabRequestItem>(entity =>
            {
                entity.ToTable("lab_request_items");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.LabRequest)
                      .WithMany(r => r.Items)
                      .HasForeignKey(e => e.LabRequestId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.ServicePrice)
                      .WithMany()
                      .HasForeignKey(e => e.ServicePriceId)
                      .OnDelete(DeleteBehavior.Restrict);
            });
            // Email Verification Code
            modelBuilder.Entity<EmailVerificationCode>(e =>
            {
                e.ToTable("email_verification_codes");
                e.HasKey("Id");

                e.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(e => e.UserId);

            });


        }
    }
}


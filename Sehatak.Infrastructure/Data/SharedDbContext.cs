using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Entities.SharedEntities;

namespace Sehatak.Infrastructure.Data
{
    public class SharedDbContext : DbContext 
    {
        public SharedDbContext(DbContextOptions<SharedDbContext> options)
        : base(options) { }
        public DbSet<SuperAdmin> SuperAdmins => Set<SuperAdmin>();
        public DbSet<MedicalCenter> MedicalCenters => Set<MedicalCenter>();
        public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
        public DbSet<CenterSubscription> CenterSubscriptions => Set<CenterSubscription>();
        public DbSet<PlatformFeature> PlatformFeatures => Set<PlatformFeature>();
        public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
        public DbSet<CenterFeature> CenterFeatures => Set<CenterFeature>();
        public DbSet<SubscriptionPayment> subscriptionPayments=> Set<SubscriptionPayment>();
        public DbSet<emailVerificationCode> emailVerificationCodes => Set<emailVerificationCode>();
        public DbSet<CenterRegistrationRequest> centerRegistrationRequests => Set<CenterRegistrationRequest>();
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<LoginAttempt> LoginAttempts { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // PlatFormFeature 
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<PlatformFeature>(entity =>
            {
                entity.ToTable("platform_features");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.NameOfFeature).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Description).HasMaxLength(300);
            });

            // PlanFeature
            modelBuilder.Entity<PlanFeature>(entity =>
            {
                entity.ToTable("plan_features");

                entity.HasKey(e => new { e.PlanId, e.FeatureId });   

                entity.HasOne(e => e.Plan)
                      .WithMany(p => p.PlanFeatures)
                      .HasForeignKey(e => e.PlanId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Feature)
                      .WithMany(f => f.PlanFeatures)
                      .HasForeignKey(e => e.FeatureId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // CenterFeature

            modelBuilder.Entity<CenterFeature>(entity =>
            {
                entity.ToTable("center_features");

                entity.HasKey(e => new { e.CenterId, e.FeatureId });   

                entity.HasOne(e => e.Center)
                      .WithMany(c => c.Features)
                      .HasForeignKey(e => e.CenterId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Feature)
                      .WithMany(f => f.CenterFeatures)
                      .HasForeignKey(e => e.FeatureId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
            //  SuperAdmin 
            modelBuilder.Entity<SuperAdmin>(entity =>
            {
                entity.ToTable("super_admins");
                entity.HasKey(e => e.Id);

                entity.HasIndex(e => e.Email).IsUnique();
                entity.HasIndex(e => e.phone).IsUnique();
                entity.Property(e => e.Name)
                      .IsRequired()
                      .HasMaxLength(100);

                entity.Property(e => e.Email)
                      .IsRequired()
                      .HasMaxLength(150);

                entity.Property(e => e.PasswordHash)
                      .IsRequired()
                      .HasMaxLength(256);

                entity.Property(e => e.phone)
                .IsRequired()
                .HasMaxLength(20);

                entity.Property(e => e.role)
                      .HasConversion<string>()
                      .HasMaxLength(100);
            });
            

            //  MedicalCenter 
            modelBuilder.Entity<MedicalCenter>(entity =>
            {
                entity.ToTable("medical_centers");
                entity.HasKey(e => e.Id);

                entity.HasIndex(e => e.UniqueUrl).IsUnique();

                entity.Property(e => e.Name)
                      .IsRequired()
                      .HasMaxLength(256);

                entity.Property(e => e.UniqueUrl)
                      .IsRequired()
                      .HasMaxLength(100);

                

                entity.Property(e=>e.Phone)
                      .IsRequired()
                      .HasMaxLength(20);

                entity.Property(e => e.PrepaymentAmount)
                      .HasPrecision(10, 2);

                entity.Property(e => e.PartialRefundPercent)
                      .HasPrecision(10, 2);

                entity.HasOne(e => e.AddedByAdmin)
                      .WithMany(s => s.AddedCenters)
                      .HasForeignKey(e => e.AddedBySuperAdminId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.Property(e => e.Address)
                      .IsRequired()
                      .HasMaxLength(500);

                entity.Property(e => e.CenterStatus)
                      .HasConversion<string>();

                entity.Property(e => e.BookingType)
                      .HasConversion<string>();

            });

            modelBuilder.Entity<CenterRegistrationRequest>(entity=>
            {
                entity.ToTable("Center_Registration_Request");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.CenterName)
                .IsRequired()
                .HasMaxLength(200);

                entity.Property(e => e.CenterAddress)
                .IsRequired()
                .HasMaxLength(300);

                entity.Property(e => e.CenterPhone)
                .IsRequired()
                .HasMaxLength(20);

                entity.Property(x => x.AdminFirstName)
                .IsRequired()
                .HasMaxLength(150);

                entity.Property(x => x.AdminLastName)
                .IsRequired()
                .HasMaxLength(150);

                entity.Property(x => x.AdminEmail)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.HasIndex(x => x.AdminEmail);

                entity.Property(x => x.AdminPhone)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(x => x.PasswordHash)
                    .IsRequired();

                entity.Property(x => x.Status)
                    .IsRequired()
                    .HasConversion<string>()   
                    .HasMaxLength(20);

                entity.Property(x => x.RejectionReason)
                    .HasMaxLength(500);

                entity.HasOne(x => x.ReviewedBySuperAdmin)
                .WithMany()
                .HasForeignKey(x => x.ReviewedBySuperAdminId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

                entity.HasOne(x => x.CreatedCenter)
               .WithOne(c => c.RegistrationRequest)   
               .HasForeignKey<CenterRegistrationRequest>(x => x.CreatedCenterId)
               .OnDelete(DeleteBehavior.Restrict)
               .IsRequired(false);

                entity.Property(x => x.PrepaymentAmount)
               .HasColumnType("decimal(10,2)");

                entity.Property(x => x.PartialRefundPercent)
                    .HasColumnType("decimal(5,2)");
            });

            modelBuilder.Entity<SubscriptionPlan>(entity =>
            {
                entity.ToTable("subscription_plans");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                      .IsRequired()
                      .HasMaxLength(100);

                entity.Property(e => e.Price)
                      .HasPrecision(10, 2);
            });
            

            modelBuilder.Entity<PlatformFeature>(entity =>
            {
                entity.ToTable("platform_features");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.NameOfFeature)
                      .IsRequired()
                      .HasMaxLength(100);



            });

            //  CenterSubscription 
            modelBuilder.Entity<CenterSubscription>(entity =>
            {
                entity.ToTable("center_subscriptions");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Center)
                      .WithMany(c => c.Subscriptions)
                      .HasForeignKey(e => e.CenterId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Plan)
                      .WithMany(p => p.CenterSubscriptions)
                      .HasForeignKey(e => e.PlanId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.Property(e => e.AmountPaid)
                      .HasPrecision(10, 2);

                entity.Property(e=>e.PaymentReference)
                        .HasMaxLength(20);

                entity.Property(e => e.Status)
                      .HasConversion<string>();
            });
            // SubscriptionPayment 
            modelBuilder.Entity<SubscriptionPayment>(entity =>
            {
                entity.ToTable("subscription_payments");
                entity.HasKey(entity => entity.Id);

                entity.Property(e => e.Amount).HasPrecision(10, 2);
                entity.Property(e => e.PaymentMethod).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ReferenceNumber).HasMaxLength(100);

                entity.HasOne(e => e.Center)
                .WithMany()
                .HasForeignKey(e => e.CenterId)
                .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Subscription)
                .WithMany()
                .HasForeignKey(e => e.SubscriptionId)
                .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.RecordedBy)
                .WithMany()
                .HasForeignKey(e => e.RecordedBySuperAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            });
            // Email Verification Code
            modelBuilder.Entity<emailVerificationCode>(e =>
            {
                e.ToTable("email_verification_codes");
                e.HasKey("Id");

                e.HasOne(e => e.superAdmin)
                .WithMany()
                .HasForeignKey(e => e.SuperAdminId)
                .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(e => e.SuperAdminId);

            });

            // LoginAttempt
            modelBuilder.Entity<LoginAttempt>(entity =>
            {
                entity.ToTable("login_attempts");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Email)
                      .IsRequired()
                      .HasMaxLength(150);

                entity.Property(e => e.IpAddress)
                      .HasMaxLength(45);

                entity.Property(e => e.UserType)
                      .HasConversion<string>()
                      .HasMaxLength(20);

                entity.HasIndex(e => new { e.CenterId, e.Email, e.AttemptedAt });
            });

        }
    }
}


using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.SharedMigrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "login_attempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CenterId = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    UserType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_login_attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "platform_features",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NameOfFeature = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_features", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Token = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OwnerType = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CenterId = table.Column<int>(type: "int", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_plans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DurationDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "super_admins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    role = table.Column<int>(type: "int", nullable: false),
                    ProfileImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreateAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_super_admins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "plan_features",
                columns: table => new
                {
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    FeatureId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_features", x => new { x.PlanId, x.FeatureId });
                    table.ForeignKey(
                        name: "FK_plan_features_platform_features_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "platform_features",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_plan_features_subscription_plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "subscription_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_verification_codes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SuperAdminId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsUsed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PendingValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_verification_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_verification_codes_super_admins_SuperAdminId",
                        column: x => x.SuperAdminId,
                        principalTable: "super_admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "medical_centers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UniqueUrl = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BookingType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequiresPrepayment = table.Column<bool>(type: "bit", nullable: false),
                    PrepaymentAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    RefundPolicyHours = table.Column<int>(type: "int", nullable: false),
                    PartialRefundPercent = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    CenterStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AddedBySuperAdminId = table.Column<int>(type: "int", nullable: true),
                    AdminWhatsappNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdminEmail = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_centers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_medical_centers_super_admins_AddedBySuperAdminId",
                        column: x => x.AddedBySuperAdminId,
                        principalTable: "super_admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "center_features",
                columns: table => new
                {
                    CenterId = table.Column<int>(type: "int", nullable: false),
                    FeatureId = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_center_features", x => new { x.CenterId, x.FeatureId });
                    table.ForeignKey(
                        name: "FK_center_features_medical_centers_CenterId",
                        column: x => x.CenterId,
                        principalTable: "medical_centers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_center_features_platform_features_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "platform_features",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Center_Registration_Request",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CenterName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CenterAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CenterPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AdminFirstName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    AdminLastName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    AdminEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    AdminPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReviewedBySuperAdminId = table.Column<int>(type: "int", nullable: true),
                    CreatedCenterId = table.Column<int>(type: "int", nullable: true),
                    RequiresPrepayment = table.Column<bool>(type: "bit", nullable: false),
                    PrepaymentAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    RefundPolicyHours = table.Column<int>(type: "int", nullable: false),
                    PartialRefundPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    logo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlanId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Center_Registration_Request", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Center_Registration_Request_medical_centers_CreatedCenterId",
                        column: x => x.CreatedCenterId,
                        principalTable: "medical_centers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Center_Registration_Request_super_admins_ReviewedBySuperAdminId",
                        column: x => x.ReviewedBySuperAdminId,
                        principalTable: "super_admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "center_subscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CenterId = table.Column<int>(type: "int", nullable: false),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AmountPaid = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    PaymentReference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_center_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_center_subscriptions_medical_centers_CenterId",
                        column: x => x.CenterId,
                        principalTable: "medical_centers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_center_subscriptions_subscription_plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "subscription_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscription_payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CenterId = table.Column<int>(type: "int", nullable: true),
                    SubscriptionId = table.Column<int>(type: "int", nullable: true),
                    RequestId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReceiptImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedBySuperAdminId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subscription_payments_Center_Registration_Request_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Center_Registration_Request",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_subscription_payments_center_subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "center_subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subscription_payments_medical_centers_CenterId",
                        column: x => x.CenterId,
                        principalTable: "medical_centers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subscription_payments_super_admins_RecordedBySuperAdminId",
                        column: x => x.RecordedBySuperAdminId,
                        principalTable: "super_admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_center_features_FeatureId",
                table: "center_features",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_Center_Registration_Request_AdminEmail",
                table: "Center_Registration_Request",
                column: "AdminEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Center_Registration_Request_CreatedCenterId",
                table: "Center_Registration_Request",
                column: "CreatedCenterId",
                unique: true,
                filter: "[CreatedCenterId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Center_Registration_Request_ReviewedBySuperAdminId",
                table: "Center_Registration_Request",
                column: "ReviewedBySuperAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_center_subscriptions_CenterId",
                table: "center_subscriptions",
                column: "CenterId");

            migrationBuilder.CreateIndex(
                name: "IX_center_subscriptions_PlanId",
                table: "center_subscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_email_verification_codes_SuperAdminId",
                table: "email_verification_codes",
                column: "SuperAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_login_attempts_CenterId_Email_AttemptedAt",
                table: "login_attempts",
                columns: new[] { "CenterId", "Email", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_medical_centers_AddedBySuperAdminId",
                table: "medical_centers",
                column: "AddedBySuperAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_centers_UniqueUrl",
                table: "medical_centers",
                column: "UniqueUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plan_features_FeatureId",
                table: "plan_features",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payments_CenterId",
                table: "subscription_payments",
                column: "CenterId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payments_RecordedBySuperAdminId",
                table: "subscription_payments",
                column: "RecordedBySuperAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payments_RequestId",
                table: "subscription_payments",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payments_SubscriptionId",
                table: "subscription_payments",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_super_admins_Email",
                table: "super_admins",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_super_admins_phone",
                table: "super_admins",
                column: "phone",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "center_features");

            migrationBuilder.DropTable(
                name: "email_verification_codes");

            migrationBuilder.DropTable(
                name: "login_attempts");

            migrationBuilder.DropTable(
                name: "plan_features");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "subscription_payments");

            migrationBuilder.DropTable(
                name: "platform_features");

            migrationBuilder.DropTable(
                name: "Center_Registration_Request");

            migrationBuilder.DropTable(
                name: "center_subscriptions");

            migrationBuilder.DropTable(
                name: "medical_centers");

            migrationBuilder.DropTable(
                name: "subscription_plans");

            migrationBuilder.DropTable(
                name: "super_admins");
        }
    }
}

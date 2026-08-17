using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Tokens;
using Sehatak.API.Hubs;
using Sehatak.API.Hubs;
using Sehatak.API.Middleware;
using Sehatak.API.Middleware;
using Sehatak.Application.Interfaces.AddDoctorDailyHours;
using Sehatak.Application.Interfaces.AddFeatureToCenter;
using Sehatak.Application.Interfaces.ApointmentInterface;
using Sehatak.Application.Interfaces.AssignFeatursToPlan;
using Sehatak.Application.Interfaces.AuditLog;
using Sehatak.Application.Interfaces.AuthPatient;
using Sehatak.Application.Interfaces.CenterRegistrationRequest;
using Sehatak.Application.Interfaces.Centers;
using Sehatak.Application.Interfaces.CentersStatusDto;
using Sehatak.Application.Interfaces.ChatInterface;
using Sehatak.Application.Interfaces.ConsultaionInterface;
using Sehatak.Application.Interfaces.DepartmentInterface;
using Sehatak.Application.Interfaces.DoctorAppointment;
using Sehatak.Application.Interfaces.Features;
using Sehatak.Application.Interfaces.GetSttafInterFace;
using Sehatak.Application.Interfaces.IAuth;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Application.Interfaces.IFinancialReports;
using Sehatak.Application.Interfaces.IPatientCenter;
using Sehatak.Application.Interfaces.IProfileInterface;
using Sehatak.Application.Interfaces.IProfileInterface.ProfileAdmin;
using Sehatak.Application.Interfaces.IShiftSchedule;
using Sehatak.Application.Interfaces.ISubscriptionPaymentService;
using Sehatak.Application.Interfaces.MedicalCenter;
using Sehatak.Application.Interfaces.MedicalCenter;
using Sehatak.Application.Interfaces.MedicalCenter;
using Sehatak.Application.Interfaces.Plans;
using Sehatak.Application.Interfaces.RemoveFeatureFromCenter;
using Sehatak.Application.Interfaces.RenewSubscription;
using Sehatak.Application.Interfaces.SearchDoctor;
using Sehatak.Application.Interfaces.ServicePriceInterface;
using Sehatak.Application.Interfaces.SignUp;
using Sehatak.Application.Interfaces.StaffLogin;
using Sehatak.Application.Interfaces.SuperAdminInterface;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.CalculateSlot;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Security;
using Sehatak.Infrastructure.Security;
using Sehatak.Infrastructure.Services;
using Sehatak.Infrastructure.Services.AddStaff;
using Sehatak.Infrastructure.Services.AppointmentService;
using Sehatak.Infrastructure.Services.Consultationservice;
using Sehatak.Infrastructure.Services.DepartmentService;
using Sehatak.Infrastructure.Services.DoctorAppointmentservice;
using Sehatak.Infrastructure.Services.EditProfileService;
using Sehatak.Infrastructure.Services.FinancialReportServices;
using Sehatak.Infrastructure.Services.GetStaff;
using Sehatak.Infrastructure.Services.PatientService.PatientRegisterAuth;
using Sehatak.Infrastructure.Services.PtientCenterService;
using Sehatak.Infrastructure.Services.SearchDoctorService;
using Sehatak.Infrastructure.Services.ServicePriceService;
using Sehatak.Infrastructure.Services.ShiftService;
using Sehatak.Infrastructure.Services.StaffLogin;
using Sehatak.Infrastructure.Services.SuperAdminService.Background;
using Sehatak.Infrastructure.Services.SuperAdminService.CenterService;
using Sehatak.Infrastructure.Services.SuperAdminService.Features.AddFeatureToCenter;
using Sehatak.Infrastructure.Services.SuperAdminService.Features.AssignFeatureToPlan;
using Sehatak.Infrastructure.Services.SuperAdminService.Features.FeatureService;
using Sehatak.Infrastructure.Services.SuperAdminService.Features.RemoveFeatureFromCenter;
using Sehatak.Infrastructure.Services.SuperAdminService.Plans;
using Sehatak.Infrastructure.Services.SuperAdminService.SubscriptionPaymentService;
using Sehatak.Infrastructure.Services.SuperAdminService.SuperAdminAuth;
using Sehatak.Infrastructure.Services.tokenService;
using Serilog;
using Serilog;
using System;
using System.Text;
using System.Threading.RateLimiting;
using System.Threading.RateLimiting;

namespace Sehatak.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 0. SERILOG — أول شي قبل أي خدمة
            builder.Host.UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(
                    path: "Logs/log-.txt",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
                ));

            // 1. CONTROLLERS
            builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
              options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            });

            // 2. SWAGGER
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                    Description = "Enter your JWT token. Example: Bearer eyJhbGci..."
                });

                options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
                {
                    {
                        new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                        {
                            Reference = new Microsoft.OpenApi.Models.OpenApiReference
                            {
                                Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                                Id   = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
            });

            // 3. LOCALIZATION
            builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

            builder.Services.Configure<RequestLocalizationOptions>(options =>
            {
                var supportedCultures = new[] { "en", "ar" };
                options.SetDefaultCulture("en")
                       .AddSupportedCultures(supportedCultures)
                       .AddSupportedUICultures(supportedCultures);
            });

            // 4. SHARED DATABASE
            builder.Services.AddDbContext<SharedDbContext>(options =>
                options.UseMySql(
                    builder.Configuration.GetConnectionString("SharedDb"),
                    ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("SharedDb"))
                )
            );

            // 5. TENANT DATABASE
            builder.Services.AddHttpContextAccessor(); // بيبني TenantDbContext لمركز معين، يدوياً
            builder.Services.AddScoped<TenantDbContextAccessor>();     // بيجيب TenantDbContext للمركز الحالي من JWT
            

            // 6. SIGNALR
            builder.Services.AddSignalR();

            // 7. CORS
            var allowedOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? [];

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("SehatakPolicy", policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                });
            });

            // 8. RATE LIMITING
            var loginPermitLimit = builder.Configuration.GetValue<int>("RateLimiting:LoginPermitLimit");
            var loginWindowSeconds = builder.Configuration.GetValue<int>("RateLimiting:LoginWindowSeconds");
            var generalPermitLimit = builder.Configuration.GetValue<int>("RateLimiting:GeneralPermitLimit");
            var generalWindowSeconds = builder.Configuration.GetValue<int>("RateLimiting:GeneralWindowSeconds");

            builder.Services.AddRateLimiter(options =>
            {
                options.AddPolicy("LoginPolicy", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = loginPermitLimit,
                            Window = TimeSpan.FromSeconds(loginWindowSeconds),
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0
                        }));

                options.AddPolicy("GeneralPolicy", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = generalPermitLimit,
                            Window = TimeSpan.FromSeconds(generalWindowSeconds),
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0
                        }));

                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = 429;
                    await context.HttpContext.Response.WriteAsJsonAsync(new
                    {
                        status = 429,
                        message = "Too many requests. Please slow down and try again later."
                    }, cancellationToken);
                };
            });


            // 9. JWT AUTHENTICATION
            var jwtKey = builder.Configuration["Jwt:SecretKey"]!;
            var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
            var jwtAudience = builder.Configuration["Jwt:Audience"]!;

            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = jwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/Hubs/ChatHubs"))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    },

                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = 401;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new
                        {
                            status = 401,
                            message = "Unauthorized. Please login first."
                        });
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = 403;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new
                        {
                            status = 403,
                            message = "Forbidden. You do not have permission to access this resource."
                        });
                    }
                };
            });

            // 10. AUTHORIZATION — ROLES
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("SuperAdminOnly", policy => policy.RequireRole("SuperAdmin"));
                options.AddPolicy("AdminOrAbove", policy => policy.RequireRole("SuperAdmin", "Admin"));
                options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
                options.AddPolicy("MedicalStaff", policy => policy.RequireRole("Admin", "Doctor", "Receptionist", "LabTechnician","Nurse","GeneralDocto"));
                options.AddPolicy("DoctorOnly", policy => policy.RequireRole("Doctor"));
                options.AddPolicy("ReceptionistOnly", policy => policy.RequireRole("Receptionist"));
                options.AddPolicy("PatientOnly", policy => policy.RequireRole("Patient"));

                
            });

            // 11. SERVICES
            builder.Services.AddSingleton<JwtTokenGenerator>();
            builder.Services.AddScoped<IAuditLogService, AuditLogService>();
            builder.Services.AddScoped<TenantMigrationRunner>();
            builder.Services.AddScoped<ISuperAdminAuthService, SuperAdminAuthService>();
            builder.Services.AddScoped<TenantDbContextFactory>();
            builder.Services.AddSingleton<Sehatak.API.Hubs.OnlineUserTracker>();

            builder.Services.AddScoped<IEmailService, EmailService>();
            builder.Services.AddScoped<IAuthService, AuthService>();
            builder.Services.AddScoped<IFeatureService, featureService>();
            builder.Services.AddScoped<ISubscriptionPlan, SubscriptionPlanService>();
            builder.Services.AddScoped<IPlanFeatureService, PlanFeatureService>();
            builder.Services.AddScoped<ICenterService, centerService>();
            builder.Services.AddScoped<IListOfCenters, listOfCentersService>();
            builder.Services.AddScoped<ISpasificCenter, SpasificCenterService>();
            builder.Services.AddScoped<IAddFeatureToCenter, AddFeatureToCenterService>();
            builder.Services.AddScoped<ICentersStatus ,CenterStatusService>();
            builder.Services.AddScoped<IRemoveFeatureFromCenter, RemoveFeatureFromCenterService>();
            builder.Services.AddScoped<IActiveFeature, ActiveFeatureService>();
            builder.Services.AddScoped<IRenewSubscription, RenewSubscriptionService>();
            builder.Services.AddScoped<IGetAllFeature, GetAllFeatureService>();
            builder.Services.AddScoped<IProfile, SuperAdminProfileService>();
            builder.Services.AddScoped<ISubscriptionPayment, SubscriptionPaymentService>();
            builder.Services.AddHostedService<SubscriptionActivationService>();
            builder.Services.AddHostedService<DelayMonitorJobs>();
            builder.Services.AddScoped<IAdminBulkEmailService, AdminBulkEmailService>();
            builder.Services.AddScoped<ICreateAdminService, CreateAdminService>();
            builder.Services.AddScoped<IStaffLogin, StaffAuthService>();
            builder.Services.AddScoped<IFinancialReport, FinancialReportService>();
            builder.Services.AddScoped<IEditPlan, EditPlanService>();
            builder.Services.AddScoped<ICenterRegistration, CenterRegistirationRequestService>();
            builder.Services.AddScoped<IDepartmentService, AddDepartmentsService>();
            builder.Services.AddScoped<IAddDoctorToDepartment, DoctorToDepartment>();
            builder.Services.AddScoped<IRemoveStaff, RemoveStaffFromCenter>();
            builder.Services.AddScoped<IListOfPlan, ListOfPlanService>();
            builder.Services.AddScoped<IGetpatientCenter, GetPatientCenterService>();
            builder.Services.AddScoped<IGetStaff, GetStaffService>();
            builder.Services.AddScoped<ISignup, AddStaffService>();
            builder.Services.AddScoped<IprofileStaff, EditStaffService>();
            builder.Services.AddScoped<IDoctorDailyHours, DoctorDailyHoursService>();
            builder.Services.AddScoped<IServicePrice, servicePrice>();
            builder.Services.AddScoped<IFinancialReportAdmin, FinancialReportAdminService>();
            builder.Services.AddScoped<IChatHub, ChatHistoryService>();
            builder.Services.AddScoped<ITokenService, TokenService>();
            builder.Services.AddScoped<ISearchDoctor, searchDoctorService>();
            builder.Services.AddScoped<IAppointment, appointmentService>();
            builder.Services.AddScoped<GenerateTheoreticalSlots>();
            builder.Services.AddScoped<IDoctorAppointment, DoctorAppointmentService>();
            builder.Services.AddScoped<IConsultation, ConsultationService>();
            builder.Services.AddScoped<IShiftSchedule, ShiftScheduleService>();


            var app = builder.Build();

            // MIDDLEWARE PIPELINE 

            app.UseMiddleware<ExceptionMiddleware>();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRequestLocalization();
            app.UseCors("SehatakPolicy");
            app.UseRouting();
            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            app.MapHub<ChatHubs>("/hubs/chat");
            app.Lifetime.ApplicationStopping.Register(() => Log.CloseAndFlush());
            
            app.Run();
        }
    }
}

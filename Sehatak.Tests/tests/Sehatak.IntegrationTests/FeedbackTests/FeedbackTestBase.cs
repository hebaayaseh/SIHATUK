using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sehatak.Application.DTOs.FeedbackDto;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using FeedbackSvc = Sehatak.Infrastructure.Services.FeedbackService.FeedbackService;

namespace Sehatak.IntegrationTests.FeedbackTests;

/// <summary>
/// Feedback lives in the SHARED database, which every test class shares, so assertions are scoped by the
/// center id of the test, by the feedback id, or by super admins created inside the test.
/// </summary>
public abstract class FeedbackTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private static int _seq;

    protected IEmailService Email { get; } = Substitute.For<IEmailService>();

    protected FeedbackSvc NewFeedback() =>
        new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, Email, NullLogger<FeedbackSvc>.Instance);

    protected static SubmitFeedbackRequestDto Req(string subject = "Printer problem", string message = "It does not print",
                                                  FeedbackType type = FeedbackType.Bug) =>
        new() { Type = type, Subject = subject, Message = message };

    protected static SubmitGuestFeedbackRequestDto GuestReq(int? centerId = null, string subject = "Question") => new()
    {
        Name = "Visitor", Email = "visitor@test.local", Phone = "0591234567", CenterId = centerId,
        Type = FeedbackType.Help, Subject = subject, Message = "How do I register?"
    };

    /// <summary>A signed-in submit from the test patient (role/name/email like the JWT claims).</summary>
    protected Task<FeedbackResponseDto> SubmitAsPatientAsync(SubmitFeedbackRequestDto? req = null) =>
        NewFeedback().SubmitAsync(C.Id, Pat.UserId, "Patient", "Pat Test", "pat@test.local", req ?? Req());

    protected async Task<int> AddSuperAdminAsync(bool active = true)
    {
        var n = Interlocked.Increment(ref _seq);
        await using var shared = Fx.Harness.NewSharedDb();
        var sa = new SuperAdmin
        {
            Name = $"FbSuper{n}", Email = $"fbsuper{n}.{Guid.NewGuid():N}@test.local", phone = $"057{n:D7}",
            role = userRole.SuperAdmin, PasswordHash = "hash-hash-hash", IsActive = active, CreateAt = DateTime.UtcNow
        };
        shared.SuperAdmins.Add(sa);
        await shared.SaveChangesAsync();
        return sa.Id;
    }

    protected async Task<Feedback> RowAsync(int feedbackId)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        return await shared.Feedbacks.AsNoTracking().SingleAsync(f => f.Id == feedbackId);
    }

    protected async Task<List<SuperAdminNotification>> AdminNotificationsAsync(int superAdminId)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        return await shared.SuperAdminNotifications.AsNoTracking()
            .Where(n => n.SuperAdminId == superAdminId).OrderBy(n => n.Id).ToListAsync();
    }
}

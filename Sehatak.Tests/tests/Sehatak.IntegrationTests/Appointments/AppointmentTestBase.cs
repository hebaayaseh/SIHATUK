using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Infrastructure.Data;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Appointments;

/// <summary>
/// Shared scenario for the appointment test classes: a fresh active center, one doctor working
/// Mondays 09:00-11:00 (30 min slots => 09:00, 09:30, 10:00, 10:30) and one patient.
/// Concrete classes must carry [Collection(SqlServerCollection.Name)] themselves.
/// </summary>
public abstract class AppointmentTestBase(SqlServerFixture fx) : IAsyncLifetime
{
    protected static readonly TimeOnly S0900 = new(9, 0);
    protected static readonly TimeOnly S0930 = new(9, 30);
    protected static readonly TimeOnly S1000 = new(10, 0);
    protected static readonly TimeOnly S1030 = new(10, 30);

    protected SqlServerFixture Fx { get; } = fx;
    protected TestCenter C { get; private set; } = null!;
    protected DoctorInfo Doc { get; private set; } = null!;
    protected PatientInfo Pat { get; private set; } = null!;
    protected DateOnly Date { get; private set; }

    public virtual async Task InitializeAsync()
    {
        C = await Fx.Harness.CreateCenterAsync();
        Doc = await C.AddDoctorAsync();
        Pat = await C.AddPatientAsync();
        Date = TestCenter.FutureDate(DayOfWeek.Monday);
        await C.AddScheduleAsync(Doc.DoctorId, DayOfWeek.Monday, S0900, new TimeOnly(11, 0), 30);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    protected TenantDbContext Db() => Fx.Harness.Tenant(C.Id);

    protected static async Task ShouldFailWith(string code, Func<Task> act)
    {
        var ex = await Should.ThrowAsync<BusinessException>(act);
        ex.Message.ShouldBe(code);
    }
}

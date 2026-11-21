using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Sehatak.IntegrationTests.Labs;
using Shouldly;

namespace Sehatak.IntegrationTests.Security;

/// <summary>Lab requests and results of a patient are visible only to the doctor who treats him.</summary>
[Collection(SqlServerCollection.Name)]
public class IdorLabTests(SqlServerFixture fx) : LabTestBase(fx)
{
    private DoctorInfo _stranger = null!;
    private int _lab;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _stranger = await C.AddDoctorAsync();
        _lab = (await SeedLabAsync(LabRequestStatus.Completed)).Id;
    }

    private static PagedRequest Page() => new() { PageNumber = 1, PageSize = 10 };

    [Fact]
    public async Task The_treating_doctor_reads_the_result_and_the_list()
    {
        (await NewLabService().DoctorGetLabResultsForPatient(C.Id, Doc.UserId, Pat.PatientId, _lab)).ShouldNotBeNull();
        (await NewLabService().GetLabRequestForPatientAsync(C.Id, Doc.UserId, Pat.PatientId, Page())).Items.ShouldNotBeEmpty();
    }

    
}

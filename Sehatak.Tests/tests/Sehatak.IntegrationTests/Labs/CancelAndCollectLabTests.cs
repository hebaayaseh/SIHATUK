using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Labs;

/// <summary>Cancel (the person who requested it) and "collect sample" (technician).</summary>
[Collection(SqlServerCollection.Name)]
public class CancelAndCollectLabTests(SqlServerFixture fx) : LabTestBase(fx)
{
    // ------------------------------------------------------------------ CancleLabRequestAsync

    [Fact]
    public async Task Doctor_cancels_his_own_Pending_request()
    {
        var (id, _) = await SeedLabAsync();

        (await NewLabService().CancleLabRequestAsync(C.Id, Doc.UserId, id)).ShouldNotBeNullOrWhiteSpace();

        var lab = await LoadLabAsync(id);
        lab.Status.ShouldBe(LabRequestStatus.Cancelled);
        ShouldBeNowUtc(lab.UpdatedAt);
        (await CountAuditAsync("CancelLabRequest", id)).ShouldBe(1);
    }

    [Fact]
    public async Task Receptionist_cancels_the_request_she_created()
    {
        var (id, _) = await SeedLabAsync(requestedBy: Rec);
        await NewLabService().CancleLabRequestAsync(C.Id, Rec, id);
        (await LoadLabAsync(id)).Status.ShouldBe(LabRequestStatus.Cancelled);
    }

    [Theory]
    [InlineData(LabRequestStatus.Collected)]
    [InlineData(LabRequestStatus.Completed)]
    public async Task Cancel_is_refused_after_the_sample_was_collected(LabRequestStatus status)
    {
        var (id, _) = await SeedLabAsync(status);
        await ShouldFailWith("LabRequest.NotFound", () => NewLabService().CancleLabRequestAsync(C.Id, Doc.UserId, id));
        (await LoadLabAsync(id)).Status.ShouldBe(status);
    }

    /// <summary>
    /// The filter only excludes Collected and Completed, so a Processing request (already PAID) can be cancelled
    /// and the money stays in the system. Fix: allow only Status == Pending.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_paid_Processing_request_cannot_be_cancelled()
    {
        var (id, _) = await SeedProcessingLabAsync();
        await ShouldFailWith("LabRequest.NotFound", () => NewLabService().CancleLabRequestAsync(C.Id, Doc.UserId, id));
        (await LoadLabAsync(id)).Status.ShouldBe(LabRequestStatus.Processing);
    }

    /// <summary>Same filter: an already Cancelled request can be "cancelled" again (second audit row, UpdatedAt moves).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Cancelling_twice_is_refused()
    {
        var (id, _) = await SeedLabAsync();
        await NewLabService().CancleLabRequestAsync(C.Id, Doc.UserId, id);

        await ShouldFailWith("LabRequest.NotFound", () => NewLabService().CancleLabRequestAsync(C.Id, Doc.UserId, id));
        (await CountAuditAsync("CancelLabRequest", id)).ShouldBe(1);
    }

    [Fact]
    public async Task Cancel_LabRequest_NotFound_for_someone_who_did_not_request_it()
    {
        var (id, _) = await SeedLabAsync();
        var doc2 = await C.AddDoctorAsync();
        await ShouldFailWith("LabRequest.NotFound", () => NewLabService().CancleLabRequestAsync(C.Id, doc2.UserId, id));
        await ShouldFailWith("LabRequest.NotFound", () => NewLabService().CancleLabRequestAsync(C.Id, Rec, id));
        (await LoadLabAsync(id)).Status.ShouldBe(LabRequestStatus.Pending);
    }

    [Fact]
    public Task Cancel_LabRequest_NotFound_for_an_unknown_id() =>
        ShouldFailWith("LabRequest.NotFound", () => NewLabService().CancleLabRequestAsync(C.Id, Doc.UserId, 999_999));

    [Fact]
    public Task Cancel_User_NotFound_for_an_unknown_user() =>
        ShouldFailWith("User.NotFound", () => NewLabService().CancleLabRequestAsync(C.Id, 999_999, 1));

    [Fact]
    public async Task Cancel_User_NotFound_for_an_inactive_user()
    {
        var inactive = await C.AddDoctorAsync(active: false);
        await ShouldFailWith("User.NotFound", () => NewLabService().CancleLabRequestAsync(C.Id, inactive.UserId, 1));
    }

    [Fact]
    public Task Cancel_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewLabService().CancleLabRequestAsync(999_999, Doc.UserId, 1));

    // ------------------------------------------------------------------ LabCollectSample

    [Fact]
    public async Task Technician_collects_the_sample()
    {
        var (id, items) = await SeedLabAsync();

        (await NewLabService().LabCollectSample(C.Id, Tech, id, null)).ShouldBe("Collected");

        var lab = await LoadLabAsync(id);
        lab.Status.ShouldBe(LabRequestStatus.Collected);
        lab.Items.ShouldAllBe(i => i.IsAvailable);
        ShouldBeNowUtc(lab.UpdatedAt);
        (await CountAuditAsync("CollectSample", id)).ShouldBe(1);
    }

    [Fact]
    public async Task Unavailable_tests_are_flagged_and_the_rest_stay_available()
    {
        var (id, items) = await SeedLabAsync();

        await NewLabService().LabCollectSample(C.Id, Tech, id, [items[0]]);

        var lab = await LoadLabAsync(id);
        lab.Items.Single(i => i.Id == items[0]).IsAvailable.ShouldBeFalse();
        lab.Items.Single(i => i.Id == items[1]).IsAvailable.ShouldBeTrue();
        lab.Status.ShouldBe(LabRequestStatus.Collected);
    }

    [Fact]
    public async Task An_empty_unavailable_list_changes_nothing()
    {
        var (id, _) = await SeedLabAsync();
        await NewLabService().LabCollectSample(C.Id, Tech, id, []);
        (await LoadLabAsync(id)).Items.ShouldAllBe(i => i.IsAvailable);
    }

    [Fact]
    public async Task NoAvailableItems_when_every_test_is_unavailable_and_nothing_is_saved()
    {
        var (id, items) = await SeedLabAsync();

        await ShouldFailWith("LabRequest.NoAvailableItems", () => NewLabService().LabCollectSample(C.Id, Tech, id, items.ToList()));

        var lab = await LoadLabAsync(id);
        lab.Status.ShouldBe(LabRequestStatus.Pending);
        lab.Items.ShouldAllBe(i => i.IsAvailable);
    }

    [Fact]
    public async Task NoAvailableItems_for_a_request_without_tests()
    {
        var (id, _) = await SeedLabAsync(noItems: true);
        await ShouldFailWith("LabRequest.NoAvailableItems", () => NewLabService().LabCollectSample(C.Id, Tech, id, null));
    }

    [Fact]
    public async Task LabRequestItemNotFound_for_an_unknown_item_and_nothing_is_saved()
    {
        var (id, items) = await SeedLabAsync();

        await ShouldFailWith("LabRequestItemNotFound", () => NewLabService().LabCollectSample(C.Id, Tech, id, [items[0], 999_999]));

        var lab = await LoadLabAsync(id);
        lab.Status.ShouldBe(LabRequestStatus.Pending);
        lab.Items.ShouldAllBe(i => i.IsAvailable);
    }

    [Fact]
    public async Task LabRequestItemNotFound_for_an_item_of_another_request()
    {
        var (id, _) = await SeedLabAsync();
        var (_, otherItems) = await SeedLabAsync();
        await ShouldFailWith("LabRequestItemNotFound", () => NewLabService().LabCollectSample(C.Id, Tech, id, [otherItems[0]]));
    }

    [Theory]
    [InlineData(LabRequestStatus.Collected)]
    [InlineData(LabRequestStatus.Processing)]
    [InlineData(LabRequestStatus.Completed)]
    [InlineData(LabRequestStatus.Cancelled)]   // the code is misleading for Cancelled ("AlreadyCollected"), but that is today's behaviour
    public async Task AlreadyCollected_unless_the_request_is_Pending(LabRequestStatus status)
    {
        var (id, _) = await SeedLabAsync(status);
        await ShouldFailWith("LabRequest.AlreadyCollected", () => NewLabService().LabCollectSample(C.Id, Tech, id, null));
        (await LoadLabAsync(id)).Status.ShouldBe(status);
    }

    /// <summary>No guard against two technicians pressing "collect" together: both succeed and the audit is written twice.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Parallel_collects_only_one_wins()
    {
        var (id, _) = await SeedLabAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { await NewLabService().LabCollectSample(C.Id, Tech, id, null); return true; } catch { return false; }
        }));

        results.Count(r => r).ShouldBe(1);
        (await CountAuditAsync("CollectSample", id)).ShouldBe(1);
    }

    [Fact]
    public Task Collect_LabRequest_NotFound() =>
        ShouldFailWith("LabRequest.NotFound", () => NewLabService().LabCollectSample(C.Id, Tech, 999_999, null));

    [Fact]
    public Task Collect_Technician_NotFound_for_an_unknown_user() =>
        ShouldFailWith("Technician.NotFound", () => NewLabService().LabCollectSample(C.Id, 999_999, 1, null));



    [Fact]
    public Task Collect_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewLabService().LabCollectSample(999_999, Tech, 1, null));

}

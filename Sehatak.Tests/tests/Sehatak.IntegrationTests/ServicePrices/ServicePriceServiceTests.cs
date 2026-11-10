using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.ServicePriceDto;
using Sehatak.Domain.Entities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using PriceSvc = Sehatak.Infrastructure.Services.ServicePriceService.ServicePriceService;

namespace Sehatak.IntegrationTests.ServicePrices;

/// <summary>
/// ServicePriceService: the price list of a center (add / list / update / remove) with a 10 minute cache.
/// Only the center admin may change prices.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ServicePriceServiceTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    // the same cache is shared by every service instance of a test, like the singleton cache in the real app
    private PriceSvc NewSvc() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, _cache);

    private int Admin => C.AdminUserId;

    private static ServicePriceRequest Req(ServiceType type, params (string Name, decimal Price)[] items) =>
        new() { Type = type, Items = items.Select(i => new ServicePriceItemDto { ServiceName = i.Name, Price = i.Price }).ToList() };

    private async Task<int> SeedAsync(string name, decimal price, ServiceType type = ServiceType.LabTest, bool active = true)
    {
        await using var db = Db();
        var s = new ServicePrice { ServiceName = name, Price = price, Type = type, IsActive = active };
        db.ServicePrices.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    private async Task<ServicePrice> RowAsync(int id)
    {
        await using var db = Db();
        return await db.ServicePrices.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    private async Task SetCenterStatusAsync(CenterStatus status)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        (await shared.MedicalCenters.SingleAsync(c => c.Id == C.Id)).CenterStatus = status;
        await shared.SaveChangesAsync();
    }

    private async Task DeactivateAdminAsync()
    {
        await using var db = Db();
        (await db.Users.SingleAsync(u => u.Id == Admin)).isActive = false;
        await db.SaveChangesAsync();
    }

    private async Task<List<string>> ListNamesAsync(ServiceType type, int page = 1, int size = 50)
    {
        var r = await NewSvc().GetServicePriceByCenterId(C.Id, type, new PagedRequest { PageNumber = page, PageSize = size });
        return r.Items.Single().Items.Select(i => i.ServiceName).ToList();
    }

    // ---------- add ----------

    [Fact]
    public async Task The_admin_adds_several_prices_and_gets_their_ids_back()
    {
        var r = await NewSvc().AddServicePriceAsync(Admin, C.Id, Req(ServiceType.LabTest, ("CBC", 30m), ("Urine", 12.5m)));

        r.Type.ShouldBe(ServiceType.LabTest);
        r.CreatedItems.Count.ShouldBe(2);
        r.CreatedItems.ShouldAllBe(i => i.Id > 0);
        var cbc = await RowAsync(r.CreatedItems.Single(i => i.ServiceName == "CBC").Id);
        cbc.Price.ShouldBe(30m);
        cbc.Type.ShouldBe(ServiceType.LabTest);
        cbc.IsActive.ShouldBeTrue();
        (await RowAsync(r.CreatedItems.Single(i => i.ServiceName == "Urine").Id)).Price.ShouldBe(12.5m);
    }

    [Fact]
    public async Task Only_an_active_admin_of_the_center_can_add()
    {
        await ShouldFailWith("Auth.Forbidden", () => NewSvc().AddServicePriceAsync(Doc.UserId, C.Id, Req(ServiceType.LabTest, ("X", 1m))));
        await ShouldFailWith("Auth.Forbidden", () => NewSvc().AddServicePriceAsync(99999, C.Id, Req(ServiceType.LabTest, ("X", 1m))));

        await DeactivateAdminAsync();
        await ShouldFailWith("Auth.Forbidden", () => NewSvc().AddServicePriceAsync(Admin, C.Id, Req(ServiceType.LabTest, ("X", 1m))));
    }

    [Theory]
    [InlineData(CenterStatus.Suspended)]
    [InlineData(CenterStatus.Pending)]
    public async Task Add_fails_for_a_center_that_is_not_active(CenterStatus status)
    {
        await SetCenterStatusAsync(status);

        await ShouldFailWith("Center.NotFound", () => NewSvc().AddServicePriceAsync(Admin, C.Id, Req(ServiceType.LabTest, ("X", 1m))));
    }

    [Fact]
    public async Task Names_repeated_inside_one_request_are_rejected_ignoring_case_and_spaces()
    {
        await ShouldFailWith("Validation.DuplicateServiceNames",
            () => NewSvc().AddServicePriceAsync(Admin, C.Id, Req(ServiceType.LabTest, ("CBC", 1m), ("  cbc ", 2m))));

        await using var db = Db();
        (await db.ServicePrices.CountAsync(s => s.Type == ServiceType.LabTest)).ShouldBe(0);
    }

    [Fact]
    public async Task A_name_that_is_already_active_in_the_same_type_is_rejected_and_nothing_is_saved()
    {
        await SeedAsync("CBC", 30m);

        await ShouldFailWith("Validation.DuplicateServiceNames",
            () => NewSvc().AddServicePriceAsync(Admin, C.Id, Req(ServiceType.LabTest, ("New one", 5m), ("cbc", 9m))));

        (await ListNamesAsync(ServiceType.LabTest)).ShouldBe(new[] { "CBC" });
    }

    [Fact]
    public async Task The_same_name_is_fine_in_another_type_or_after_the_old_one_was_removed()
    {
        await SeedAsync("Bandage", 3m, ServiceType.MedicalSupply);
        await SeedAsync("Old test", 3m, ServiceType.LabTest, active: false);

        await NewSvc().AddServicePriceAsync(Admin, C.Id, Req(ServiceType.Other, ("Bandage", 4m)));
        await NewSvc().AddServicePriceAsync(Admin, C.Id, Req(ServiceType.LabTest, ("Old test", 8m)));

        (await ListNamesAsync(ServiceType.Other)).ShouldBe(new[] { "Bandage" });
        (await ListNamesAsync(ServiceType.LabTest)).ShouldBe(new[] { "Old test" });
    }

    [Theory]
    [InlineData(ServiceType.ConsultationCost)]
    [InlineData(ServiceType.Appointment)]
    [InlineData(ServiceType.FollowUp)]
    public async Task These_types_hold_one_price_only_a_second_add_is_rejected_until_it_is_removed(ServiceType type)
    {
        var first = await NewSvc().AddServicePriceAsync(Admin, C.Id, Req(type, ("Price", 50m)));

        await ShouldFailWith("ServiceType.AlreadyExist", () => NewSvc().AddServicePriceAsync(Admin, C.Id, Req(type, ("Other name", 60m))));

        await NewSvc().RemoveServicePrice(Admin, C.Id, first.CreatedItems.Single().Id);
        var again = await NewSvc().AddServicePriceAsync(Admin, C.Id, Req(type, ("Other name", 60m)));
        again.CreatedItems.Single().ServiceName.ShouldBe("Other name");
    }

    [Theory]
    [InlineData(ServiceType.LabTest)]
    [InlineData(ServiceType.MedicalSupply)]
    [InlineData(ServiceType.Other)]
    public async Task These_types_hold_many_prices(ServiceType type)
    {
        await NewSvc().AddServicePriceAsync(Admin, C.Id, Req(type, ("A", 1m)));
        await NewSvc().AddServicePriceAsync(Admin, C.Id, Req(type, ("B", 2m)));

        (await ListNamesAsync(type)).Count.ShouldBe(2);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Names_are_saved_without_the_spaces_around_them()
    {
        // the duplicate check trims, update trims, add does not: "  CBC " is saved as is
        var r = await NewSvc().AddServicePriceAsync(Admin, C.Id, Req(ServiceType.LabTest, ("  CBC  ", 30m)));

        (await RowAsync(r.CreatedItems.Single().Id)).ServiceName.ShouldBe("CBC");
    }

    // ---------- list ----------

    [Fact]
    public async Task The_list_has_only_active_prices_of_the_asked_type()
    {
        await SeedAsync("CBC", 30m);
        await SeedAsync("Hidden", 1m, active: false);
        await SeedAsync("Gloves", 2m, ServiceType.MedicalSupply);

        var r = await NewSvc().GetServicePriceByCenterId(C.Id, ServiceType.LabTest, new PagedRequest());

        r.TotalCount.ShouldBe(1);
        r.Items.Single().Type.ShouldBe(ServiceType.LabTest);
        r.Items.Single().Items.Single().ServiceName.ShouldBe("CBC");
        r.Items.Single().Items.Single().Price.ShouldBe(30m);
    }

    [Fact]
    public async Task Pages_do_not_repeat_or_skip_items()
    {
        for (var i = 1; i <= 12; i++) await SeedAsync($"Test {i:D2}", i);

        var p1 = await ListNamesAsync(ServiceType.LabTest, 1, 5);
        var p2 = await ListNamesAsync(ServiceType.LabTest, 2, 5);
        var p3 = await ListNamesAsync(ServiceType.LabTest, 3, 5);

        p1.Count.ShouldBe(5);
        p2.Count.ShouldBe(5);
        p3.Count.ShouldBe(2);
        p1.Concat(p2).Concat(p3).Distinct().Count().ShouldBe(12);

        var r = await NewSvc().GetServicePriceByCenterId(C.Id, ServiceType.LabTest, new PagedRequest { PageNumber = 3, PageSize = 5 });
        r.TotalCount.ShouldBe(12);
        r.PageNumber.ShouldBe(3);
    }

    [Fact]
    public async Task An_empty_type_gives_an_empty_page_not_an_error()
    {
        var r = await NewSvc().GetServicePriceByCenterId(C.Id, ServiceType.Other, new PagedRequest());

        r.TotalCount.ShouldBe(0);
        r.Items.Single().Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(CenterStatus.Suspended)]
    [InlineData(CenterStatus.Pending)]
    public async Task The_list_fails_for_a_center_that_is_not_active(CenterStatus status)
    {
        await SetCenterStatusAsync(status);

        await ShouldFailWith("Center.NotFound", () => NewSvc().GetServicePriceByCenterId(C.Id, ServiceType.LabTest, new PagedRequest()));
    }

    [Fact]
    public async Task An_unknown_center_is_a_clean_error_on_the_list()
    {
        await ShouldFailWith("Center.NotFound", () => NewSvc().GetServicePriceByCenterId(987654, ServiceType.LabTest, new PagedRequest()));
    }

    [Fact]
    public async Task The_list_of_one_center_never_shows_the_prices_of_another()
    {
        await SeedAsync("Mine", 1m);
        var other = await Fx.Harness.CreateCenterAsync();
        await using (var db = Fx.Harness.Tenant(other.Id))
        {
            db.ServicePrices.Add(new ServicePrice { ServiceName = "Theirs", Price = 9m, Type = ServiceType.LabTest });
            await db.SaveChangesAsync();
        }

        (await ListNamesAsync(ServiceType.LabTest)).ShouldBe(new[] { "Mine" });
        var theirs = await NewSvc().GetServicePriceByCenterId(other.Id, ServiceType.LabTest, new PagedRequest());
        theirs.Items.Single().Items.Select(i => i.ServiceName).ShouldBe(new[] { "Theirs" });
    }

    // ---------- cache ----------

    [Fact]
    public async Task The_second_read_comes_from_the_cache()
    {
        await SeedAsync("CBC", 30m);
        var first = await NewSvc().GetServicePriceByCenterId(C.Id, ServiceType.LabTest, new PagedRequest());

        // changed behind the service's back: only the cache can still show the old value
        await using (var db = Db()) { (await db.ServicePrices.SingleAsync()).Price = 99m; await db.SaveChangesAsync(); }
        var second = await NewSvc().GetServicePriceByCenterId(C.Id, ServiceType.LabTest, new PagedRequest());

        second.ShouldBeSameAs(first);
        second.Items.Single().Items.Single().Price.ShouldBe(30m);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_new_price_shows_in_the_list_right_after_it_is_added()
    {
        // InvalidateServicePriceCache only clears its own key list, it never removes the entries from IMemoryCache
        await SeedAsync("CBC", 30m);
        await ListNamesAsync(ServiceType.LabTest);

        await NewSvc().AddServicePriceAsync(Admin, C.Id, Req(ServiceType.LabTest, ("Urine", 12m)));

        (await ListNamesAsync(ServiceType.LabTest)).ShouldContain("Urine");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_changed_price_shows_in_the_list_right_after_it_is_updated()
    {
        var id = await SeedAsync("CBC", 30m);
        await ListNamesAsync(ServiceType.LabTest);

        await NewSvc().updateServicePrice(Admin, C.Id, new UpdateServicePrice { ServicePriceId = id, Price = 45m });

        var r = await NewSvc().GetServicePriceByCenterId(C.Id, ServiceType.LabTest, new PagedRequest());
        r.Items.Single().Items.Single().Price.ShouldBe(45m);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_removed_price_disappears_from_the_list_right_after_it_is_removed()
    {
        var id = await SeedAsync("CBC", 30m);
        await ListNamesAsync(ServiceType.LabTest);

        await NewSvc().RemoveServicePrice(Admin, C.Id, id);

        (await ListNamesAsync(ServiceType.LabTest)).ShouldBeEmpty();
    }

    // ---------- update ----------

    [Fact]
    public async Task Update_changes_price_and_name_and_trims_the_name()
    {
        var id = await SeedAsync("CBC", 30m);
        var before = (await RowAsync(id)).UpdatedAt;
        await Task.Delay(20);

        var r = await NewSvc().updateServicePrice(Admin, C.Id, new UpdateServicePrice { ServicePriceId = id, ServiceName = "  Full blood count ", Price = 45.5m });

        r.Id.ShouldBe(id);
        r.ServiceName.ShouldBe("Full blood count");
        r.Price.ShouldBe(45.5m);
        r.Type.ShouldBe(ServiceType.LabTest);
        var row = await RowAsync(id);
        row.ServiceName.ShouldBe("Full blood count");
        row.Price.ShouldBe(45.5m);
        row.UpdatedAt.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task Update_with_only_a_price_keeps_the_name_and_the_other_way_round()
    {
        var id = await SeedAsync("CBC", 30m);

        await NewSvc().updateServicePrice(Admin, C.Id, new UpdateServicePrice { ServicePriceId = id, Price = 31m, ServiceName = null });
        (await RowAsync(id)).ServiceName.ShouldBe("CBC");
        (await RowAsync(id)).Price.ShouldBe(31m);

        await NewSvc().updateServicePrice(Admin, C.Id, new UpdateServicePrice { ServicePriceId = id, ServiceName = "Blood count", Price = null });
        (await RowAsync(id)).ServiceName.ShouldBe("Blood count");
        (await RowAsync(id)).Price.ShouldBe(31m);
    }

    [Fact]
    public async Task A_blank_name_is_ignored_and_zero_is_a_valid_price()
    {
        var id = await SeedAsync("CBC", 30m);

        await NewSvc().updateServicePrice(Admin, C.Id, new UpdateServicePrice { ServicePriceId = id, ServiceName = "   ", Price = 0m });

        var row = await RowAsync(id);
        row.ServiceName.ShouldBe("CBC");
        row.Price.ShouldBe(0m);
    }

    [Fact]
    public async Task A_negative_price_is_rejected_and_nothing_changes()
    {
        var id = await SeedAsync("CBC", 30m);

        await ShouldFailWith("ServicePrice.InvalidPrice",
            () => NewSvc().updateServicePrice(Admin, C.Id, new UpdateServicePrice { ServicePriceId = id, ServiceName = "Changed", Price = -1m }));

        var row = await RowAsync(id);
        row.Price.ShouldBe(30m);
        row.ServiceName.ShouldBe("CBC");
    }

    [Fact]
    public async Task Update_only_works_for_an_active_admin_an_active_price_and_an_active_center()
    {
        var id = await SeedAsync("CBC", 30m);
        var removed = await SeedAsync("Gone", 5m, active: false);
        var req = new UpdateServicePrice { ServicePriceId = id, Price = 1m };

        await ShouldFailWith("Auth.Forbidden", () => NewSvc().updateServicePrice(Doc.UserId, C.Id, req));
        await ShouldFailWith("General.NotFound", () => NewSvc().updateServicePrice(Admin, C.Id, new UpdateServicePrice { ServicePriceId = removed, Price = 1m }));
        await ShouldFailWith("General.NotFound", () => NewSvc().updateServicePrice(Admin, C.Id, new UpdateServicePrice { ServicePriceId = 99999, Price = 1m }));

        await SetCenterStatusAsync(CenterStatus.Suspended);
        await ShouldFailWith("Center.NotFound", () => NewSvc().updateServicePrice(Admin, C.Id, req));

        (await RowAsync(id)).Price.ShouldBe(30m);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_price_cannot_be_renamed_to_a_name_that_is_already_active_in_its_type()
    {
        // add refuses duplicate names, rename does not check
        await SeedAsync("CBC", 30m);
        var urine = await SeedAsync("Urine", 10m);

        await ShouldFailWith("Validation.DuplicateServiceNames",
            () => NewSvc().updateServicePrice(Admin, C.Id, new UpdateServicePrice { ServicePriceId = urine, ServiceName = "cbc" }));
    }

    // ---------- remove ----------

    [Fact]
    public async Task Remove_hides_the_price_but_keeps_the_row_for_old_invoices()
    {
        var id = await SeedAsync("CBC", 30m);
        var before = (await RowAsync(id)).UpdatedAt;
        await Task.Delay(20);

        var msg = await NewSvc().RemoveServicePrice(Admin, C.Id, id);

        msg.ShouldNotBeNullOrWhiteSpace();
        var row = await RowAsync(id);
        row.IsActive.ShouldBeFalse();
        row.UpdatedAt.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task Remove_only_works_for_an_active_admin_and_an_active_price()
    {
        var id = await SeedAsync("CBC", 30m);

        await ShouldFailWith("Auth.Forbidden", () => NewSvc().RemoveServicePrice(Doc.UserId, C.Id, id));
        await ShouldFailWith("General.NotFound", () => NewSvc().RemoveServicePrice(Admin, C.Id, 99999));

        await NewSvc().RemoveServicePrice(Admin, C.Id, id);
        await ShouldFailWith("General.NotFound", () => NewSvc().RemoveServicePrice(Admin, C.Id, id));

        await DeactivateAdminAsync();
        var other = await SeedAsync("Other", 1m);
        await ShouldFailWith("Auth.Forbidden", () => NewSvc().RemoveServicePrice(Admin, C.Id, other));
        (await RowAsync(other)).IsActive.ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Remove_fails_for_a_center_that_is_not_active()
    {
        // the center lookup is not awaited, so the check compares a Task with null and never fails
        var id = await SeedAsync("CBC", 30m);
        await SetCenterStatusAsync(CenterStatus.Suspended);

        await ShouldFailWith("Center.NotFound", () => NewSvc().RemoveServicePrice(Admin, C.Id, id));

        (await RowAsync(id)).IsActive.ShouldBeTrue();
    }
}

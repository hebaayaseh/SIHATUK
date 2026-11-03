using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.MonthlySalaryDto;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Finance;

[Collection(SqlServerCollection.Name)]
public class SalaryTests(SqlServerFixture fx) : FinanceTestBase(fx)
{
    private static SetCategorySalaryRequestDto Cat(SalaryCategory c, decimal amount, int month = 5) =>
        new() { Year = 2025, Month = month, Category = c, Amount = amount };

    // ---------- category salaries ----------

    [Fact]
    public async Task Setting_a_category_twice_updates_the_same_row()
    {
        await NewSalaries().SetCategorySalaryAsync(C.Id, Admin, Cat(SalaryCategory.Nursing, 1000m));
        var r = await NewSalaries().SetCategorySalaryAsync(C.Id, Admin, Cat(SalaryCategory.Nursing, 1500m));

        r.Category.ShouldBe("Nursing");
        r.Amount.ShouldBe(1500m);
        await using var db = Db();
        (await db.MonthlySalaries.CountAsync(s => s.Year == 2025 && s.Month == 5)).ShouldBe(1);
    }

    [Fact]
    public async Task Zero_is_allowed_negative_is_not()
    {
        (await NewSalaries().SetCategorySalaryAsync(C.Id, Admin, Cat(SalaryCategory.Lab, 0m))).Amount.ShouldBe(0m);
        await ShouldFailWith("Validation.InvalidAmount", () => NewSalaries().SetCategorySalaryAsync(C.Id, Admin, Cat(SalaryCategory.Lab, -1m)));
    }

    [Theory]
    [InlineData(0, 5, "Validation.InvalidYear")]
    [InlineData(10000, 5, "Validation.InvalidYear")]
    [InlineData(2025, 0, "Validation.InvalidMonth")]
    [InlineData(2025, 13, "Validation.InvalidMonth")]
    public Task Bad_year_or_month_is_rejected(int year, int month, string code) =>
        ShouldFailWith(code, () => NewSalaries().SetCategorySalaryAsync(C.Id, Admin,
            new SetCategorySalaryRequestDto { Year = year, Month = month, Category = SalaryCategory.Lab, Amount = 1m }));

    [Fact]
    public Task Unknown_category_is_rejected() =>
        ShouldFailWith("Validation.InvalidCategory", () => NewSalaries().SetCategorySalaryAsync(C.Id, Admin, Cat((SalaryCategory)77, 1m)));

    [Fact]
    public async Task Only_an_admin_can_set_or_read_salaries()
    {
        await ShouldFailWith("Auth.Forbidden", () => NewSalaries().SetCategorySalaryAsync(C.Id, Rec, Cat(SalaryCategory.Lab, 1m)));
        await ShouldFailWith("Auth.Forbidden", () => NewSalaries().GetSalariesAsync(C.Id, Rec, 2025, 5));
        await ShouldFailWith("Center.NotFound", () => NewSalaries().GetSalariesAsync(999_999, Admin, 2025, 5));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_saves_of_the_same_category_at_once_both_succeed_with_one_row()
    {
        var results = await Task.WhenAll(Enumerable.Range(1, 2).Select(async i =>
        {
            try { await NewSalaries().SetCategorySalaryAsync(C.Id, Admin, Cat(SalaryCategory.Nursing, i * 100m)); return (Exception?)null; }
            catch (Exception e) { return e; }
        }));

        results.ShouldAllBe(e => e == null);
        await using var db = Db();
        (await db.MonthlySalaries.CountAsync()).ShouldBe(1);
    }

    // ---------- doctor salaries ----------

    [Fact]
    public async Task Doctor_salary_is_created_then_updated()
    {
        await NewSalaries().SetDoctorSalaryAsync(C.Id, Admin, new SetDoctorSalaryRequestDto { DoctorId = Doc.DoctorId, Year = 2025, Month = 5, Amount = 3000m });
        var r = await NewSalaries().SetDoctorSalaryAsync(C.Id, Admin, new SetDoctorSalaryRequestDto { DoctorId = Doc.DoctorId, Year = 2025, Month = 5, Amount = 3500m });

        r.DoctorId.ShouldBe(Doc.DoctorId);
        r.Amount.ShouldBe(3500m);
        r.DoctorName.ShouldBe(await DoctorNameAsync());
        await using var db = Db();
        (await db.DoctorSalaries.CountAsync()).ShouldBe(1);
    }

    private async Task<string> DoctorNameAsync()
    {
        await using var db = Db();
        var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == Doc.UserId);
        return $"{u.firstName} {u.lastName}";
    }

    [Fact]
    public async Task Doctor_salary_for_unknown_or_inactive_doctor_is_rejected()
    {
        await ShouldFailWith("Doctor.NotFound", () => NewSalaries().SetDoctorSalaryAsync(C.Id, Admin,
            new SetDoctorSalaryRequestDto { DoctorId = 999_999, Year = 2025, Month = 5, Amount = 1m }));

        var inactive = await C.AddDoctorAsync(active: false);
        await ShouldFailWith("Doctor.NotFound", () => NewSalaries().SetDoctorSalaryAsync(C.Id, Admin,
            new SetDoctorSalaryRequestDto { DoctorId = inactive.DoctorId, Year = 2025, Month = 5, Amount = 1m }));
    }

    [Fact]
    public Task Doctor_salary_negative_is_rejected() =>
        ShouldFailWith("Validation.InvalidAmount", () => NewSalaries().SetDoctorSalaryAsync(C.Id, Admin,
            new SetDoctorSalaryRequestDto { DoctorId = Doc.DoctorId, Year = 2025, Month = 5, Amount = -1m }));

    // ---------- monthly view ----------

    [Fact]
    public async Task Month_view_lists_every_category_even_when_unset_and_sums_correctly()
    {
        await NewSalaries().SetCategorySalaryAsync(C.Id, Admin, Cat(SalaryCategory.Nursing, 1000m));
        await NewSalaries().SetCategorySalaryAsync(C.Id, Admin, Cat(SalaryCategory.Lab, 500m));
        await NewSalaries().SetDoctorSalaryAsync(C.Id, Admin, new SetDoctorSalaryRequestDto { DoctorId = Doc.DoctorId, Year = 2025, Month = 5, Amount = 2000m });

        var v = await NewSalaries().GetSalariesAsync(C.Id, Admin, 2025, 5);

        v.Categories.Count.ShouldBe(3);
        v.Categories.Single(c => c.Category == "GeneralMedicine").Amount.ShouldBe(0m);
        v.CategoriesTotal.ShouldBe(1500m);
        v.DoctorsTotal.ShouldBe(2000m);
        v.TotalSalaries.ShouldBe(3500m);
    }

    [Fact]
    public async Task Month_view_does_not_leak_other_months()
    {
        await NewSalaries().SetCategorySalaryAsync(C.Id, Admin, Cat(SalaryCategory.Nursing, 1000m, month: 4));
        (await NewSalaries().GetSalariesAsync(C.Id, Admin, 2025, 5)).TotalSalaries.ShouldBe(0m);
    }

    [Fact]
    public async Task Inactive_doctor_shows_only_in_months_where_they_have_a_salary()
    {
        var doc2 = await C.AddDoctorAsync();
        await NewSalaries().SetDoctorSalaryAsync(C.Id, Admin, new SetDoctorSalaryRequestDto { DoctorId = doc2.DoctorId, Year = 2025, Month = 5, Amount = 900m });
        await using (var db = Db())
        {
            var u = await db.Users.SingleAsync(x => x.Id == doc2.UserId);
            u.isActive = false;
            await db.SaveChangesAsync();
        }

        (await NewSalaries().GetSalariesAsync(C.Id, Admin, 2025, 5)).Doctors.ShouldContain(d => d.DoctorId == doc2.DoctorId);
        (await NewSalaries().GetSalariesAsync(C.Id, Admin, 2025, 6)).Doctors.ShouldNotContain(d => d.DoctorId == doc2.DoctorId);
    }
}

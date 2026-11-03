using Sehatak.Application.Common;
using Sehatak.Application.DTOs.ExpenseDto;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Finance;

[Collection(SqlServerCollection.Name)]
public class ExpenseTests(SqlServerFixture fx) : FinanceTestBase(fx)
{
    private static readonly DateOnly D = new(2025, 5, 10);

    private static ExpenseRequestDto Req(decimal amount = 50m, ExpenseSection s = ExpenseSection.Center, string? name = "Rent") =>
        new() { Name = name, Amount = amount, Section = s, ExpenseDate = D };

    // ---------- Add ----------

    [Fact]
    public async Task Add_saves_the_expense_with_trimmed_name_and_creator()
    {
        var r = await NewExpenses().AddExpenseAsync(C.Id, Admin, Req(name: "  Rent  "));

        r.Name.ShouldBe("Rent");
        r.Amount.ShouldBe(50m);
        r.Section.ShouldBe("Center");
        r.CreatedByUserId.ShouldBe(Admin);
        r.ExpenseDate.ShouldBe(D);
        r.Id.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Add_blank_name_is_stored_as_null(string? name)
    {
        var r = await NewExpenses().AddExpenseAsync(C.Id, Admin, Req(name: name));
        r.Name.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public Task Add_non_positive_amount_is_rejected(decimal amount) =>
        ShouldFailWith("Validation.InvalidAmount", () => NewExpenses().AddExpenseAsync(C.Id, Admin, Req(amount)));

    [Fact]
    public Task Add_unknown_section_is_rejected() =>
        ShouldFailWith("Validation.InvalidSection", () => NewExpenses().AddExpenseAsync(C.Id, Admin, Req(s: (ExpenseSection)99)));

    [Fact]
    public Task Add_name_longer_than_150_is_rejected() =>
        ShouldFailWith("Validation.NameTooLong", () => NewExpenses().AddExpenseAsync(C.Id, Admin, Req(name: new string('x', 151))));

    [Fact]
    [Trait("Category", "KnownBug")]
    public Task Add_amount_bigger_than_the_column_gives_a_business_error_not_a_crash() =>
        ShouldFailWith("Validation.InvalidAmount", () => NewExpenses().AddExpenseAsync(C.Id, Admin, Req(1_000_000_000m)));

    [Fact]
    public async Task Only_an_active_admin_can_manage_expenses()
    {
        await ShouldFailWith("Auth.Forbidden", () => NewExpenses().AddExpenseAsync(C.Id, Rec, Req()));
        await ShouldFailWith("Auth.Forbidden", () => NewExpenses().AddExpenseAsync(C.Id, 999_999, Req()));
    }

    [Fact]
    public Task Unknown_center_is_rejected() =>
        ShouldFailWith("Center.NotFound", () => NewExpenses().AddExpenseAsync(999_999, Admin, Req()));

    // ---------- Update ----------

    [Fact]
    public async Task Update_changes_only_the_fields_sent()
    {
        var id = await AddExpenseRowAsync(D, 100m, ExpenseSection.Lab, "Reagents");

        var r = await NewExpenses().UpdateExpenseAsync(C.Id, Admin, id, new UpdateExpenseRequestDto { Amount = 120m });

        r.Amount.ShouldBe(120m);
        r.Name.ShouldBe("Reagents");
        r.Section.ShouldBe("Lab");
        r.ExpenseDate.ShouldBe(D);
    }

    [Fact]
    public async Task Update_can_move_section_and_date()
    {
        var id = await AddExpenseRowAsync(D, 100m, ExpenseSection.Lab);
        var r = await NewExpenses().UpdateExpenseAsync(C.Id, Admin, id,
            new UpdateExpenseRequestDto { Section = ExpenseSection.Center, ExpenseDate = D.AddDays(1) });
        r.Section.ShouldBe("Center");
        r.ExpenseDate.ShouldBe(D.AddDays(1));
    }

    [Fact]
    public Task Update_unknown_expense_is_rejected() =>
        ShouldFailWith("Expense.NotFound", () => NewExpenses().UpdateExpenseAsync(C.Id, Admin, 999_999, new UpdateExpenseRequestDto { Amount = 5m }));

    [Fact]
    public async Task Update_with_bad_values_is_rejected()
    {
        var id = await AddExpenseRowAsync(D, 100m, ExpenseSection.Lab);
        await ShouldFailWith("Validation.InvalidAmount", () => NewExpenses().UpdateExpenseAsync(C.Id, Admin, id, new UpdateExpenseRequestDto { Amount = 0m }));
        await ShouldFailWith("Validation.InvalidSection", () => NewExpenses().UpdateExpenseAsync(C.Id, Admin, id, new UpdateExpenseRequestDto { Section = (ExpenseSection)42 }));
        await ShouldFailWith("Validation.NameTooLong", () => NewExpenses().UpdateExpenseAsync(C.Id, Admin, id, new UpdateExpenseRequestDto { Name = new string('y', 200) }));
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_removes_the_expense()
    {
        var id = await AddExpenseRowAsync(D, 100m, ExpenseSection.Lab);
        await NewExpenses().DeleteExpenseAsync(C.Id, Admin, id);

        await using var db = Db();
        db.Expenses.Any(e => e.Id == id).ShouldBeFalse();
    }

    [Fact]
    public Task Delete_unknown_expense_is_rejected() =>
        ShouldFailWith("Expense.NotFound", () => NewExpenses().DeleteExpenseAsync(C.Id, Admin, 999_999));

    // ---------- Query ----------

    [Fact]
    public async Task Get_returns_only_the_month_newest_first_with_inclusive_boundaries()
    {
        var first = await AddExpenseRowAsync(new DateOnly(2025, 5, 1), 1m, ExpenseSection.Lab);
        var mid = await AddExpenseRowAsync(new DateOnly(2025, 5, 15), 2m, ExpenseSection.Lab);
        var last = await AddExpenseRowAsync(new DateOnly(2025, 5, 31), 3m, ExpenseSection.Lab);
        await AddExpenseRowAsync(new DateOnly(2025, 4, 30), 9m, ExpenseSection.Lab);
        await AddExpenseRowAsync(new DateOnly(2025, 6, 1), 9m, ExpenseSection.Lab);

        var page = await NewExpenses().GetExpensesAsync(C.Id, Admin, 2025, 5, null, new PagedRequest());

        page.Items.Select(i => i.Id).ShouldBe(new[] { last, mid, first });
        page.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task Get_filters_by_section_and_pages()
    {
        for (var i = 1; i <= 3; i++) await AddExpenseRowAsync(new DateOnly(2025, 5, i), i, ExpenseSection.Lab);
        await AddExpenseRowAsync(new DateOnly(2025, 5, 9), 50m, ExpenseSection.Center);

        var page = await NewExpenses().GetExpensesAsync(C.Id, Admin, 2025, 5, ExpenseSection.Lab, new PagedRequest { PageNumber = 2, PageSize = 2 });

        page.TotalCount.ShouldBe(3);
        page.Items.Count.ShouldBe(1);
        page.Items.Single().Amount.ShouldBe(1m);
    }

    [Theory]
    [InlineData(0, 5, "Validation.InvalidYear")]
    [InlineData(2025, 0, "Validation.InvalidMonth")]
    [InlineData(2025, 13, "Validation.InvalidMonth")]
    public Task Get_with_bad_year_or_month_is_rejected(int year, int month, string code) =>
        ShouldFailWith(code, () => NewExpenses().GetExpensesAsync(C.Id, Admin, year, month, null, new PagedRequest()));
}

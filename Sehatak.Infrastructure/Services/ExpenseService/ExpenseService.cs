using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.ExpenseDto;
using Sehatak.Application.Interfaces.IExpense;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.ExpenseService
{
    public class ExpenseService : IExpense
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;

        public ExpenseService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        private async Task<TenantDbContext> OpenAdminDbAsync(int centerId, int userId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            var db = contextFactory.CreateForCenter(centerId);

            var admin = await db.Users
                .FirstOrDefaultAsync(a => a.Id == userId
                                     && a.role == userRole.Admin
                                     && a.isActive);

            if (admin == null)
            {
                db.Dispose();
                throw new BusinessException("Auth.Forbidden");
            }

            return db;
        }

        private static ExpenseResponseDto ToDto(Expense e) => new ExpenseResponseDto
        {
            Id = e.Id,
            Name = e.Name,
            Amount = e.Amount,
            Section = e.Section.ToString(),
            ExpenseDate = e.ExpenseDate,
            CreatedByUserId = e.CreatedByUserId,
            CreatedAt = e.CreatedAt
        };

        public async Task<ExpenseResponseDto> AddExpenseAsync(int centerId, int userId, ExpenseRequestDto request)
        {
            if (request.Amount <= 0)
                throw new BusinessException("Validation.InvalidAmount");

            if (!Enum.IsDefined(request.Section))
                throw new BusinessException("Validation.InvalidSection");

            if (request.Name != null && request.Name.Length > 150)
                throw new BusinessException("Validation.NameTooLong");

            using var db = await OpenAdminDbAsync(centerId, userId);

            var expense = new Expense
            {
                Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
                Amount = request.Amount,
                Section = request.Section,
                ExpenseDate = request.ExpenseDate,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            await db.Expenses.AddAsync(expense);
            await db.SaveChangesAsync();

            return ToDto(expense);
        }

        public async Task<ExpenseResponseDto> UpdateExpenseAsync(int centerId, int userId, int expenseId , UpdateExpenseRequestDto request)
        {
            if (request.Amount != null && request.Amount <= 0)
                throw new BusinessException("Validation.InvalidAmount");

            if (request.Section != null && !Enum.IsDefined(request.Section.Value))
                throw new BusinessException("Validation.InvalidSection");

            if (request.Name != null && request.Name.Length > 150)
                throw new BusinessException("Validation.NameTooLong");

            using var db = await OpenAdminDbAsync(centerId, userId);

            var expense = await db.Expenses
                .FirstOrDefaultAsync(e => e.Id == expenseId);

            if (expense == null)
                throw new BusinessException("Expense.NotFound");

            if (request.Name != null)
                expense.Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();

            if (request.Amount != null)
                expense.Amount = request.Amount.Value;

            if (request.Section != null)
                expense.Section = request.Section.Value;

            if (request.ExpenseDate != null)
                expense.ExpenseDate = request.ExpenseDate.Value;

            await db.SaveChangesAsync();

            return ToDto(expense);
        }

        public async Task<string> DeleteExpenseAsync(int centerId, int userId, int expenseId)
        {
            using var db = await OpenAdminDbAsync(centerId, userId);

            var expense = await db.Expenses
                .FirstOrDefaultAsync(e => e.Id == expenseId);

            if (expense == null)
                throw new BusinessException("Expense.NotFound");

            db.Expenses.Remove(expense);
            await db.SaveChangesAsync();

            return "تم حذف المصروف بنجاح.";
        }

        public async Task<PagedResult<ExpenseResponseDto>> GetExpensesAsync(int centerId, int userId, int year, int month, ExpenseSection? section, PagedRequest request)
        {
            if (year < 1 || year > 9999)
                throw new BusinessException("Validation.InvalidYear");

            if (month < 1 || month > 12)
                throw new BusinessException("Validation.InvalidMonth");

            using var db = await OpenAdminDbAsync(centerId, userId);

            var start = new DateOnly(year, month, 1);
            var end = start.AddMonths(1);

            var query = db.Expenses
                .Where(e => e.ExpenseDate >= start
                       && e.ExpenseDate < end
                       && (section == null || e.Section == section))
                .OrderByDescending(e => e.ExpenseDate)
                .ThenByDescending(e => e.Id)
                .Select(e => new ExpenseResponseDto
                {
                    Id = e.Id,
                    Name = e.Name,
                    Amount = e.Amount,
                    Section = e.Section.ToString(),
                    ExpenseDate = e.ExpenseDate,
                    CreatedByUserId = e.CreatedByUserId,
                    CreatedAt = e.CreatedAt
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }
    }
}

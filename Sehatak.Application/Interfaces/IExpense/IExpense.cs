using Sehatak.Application.Common;
using Sehatak.Application.DTOs.ExpenseDto;
using Sehatak.Domain.Enums;

namespace Sehatak.Application.Interfaces.IExpense
{
    public interface IExpense
    {
        Task<ExpenseResponseDto> AddExpenseAsync(int centerId, int userId, ExpenseRequestDto request);
        Task<ExpenseResponseDto> UpdateExpenseAsync(int centerId, int userId,int expenseId , UpdateExpenseRequestDto request);
        Task<string> DeleteExpenseAsync(int centerId, int userId, int expenseId);
        Task<PagedResult<ExpenseResponseDto>> GetExpensesAsync(int centerId, int userId, int year, int month, ExpenseSection? section, PagedRequest request);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.ExpenseDto;
using Sehatak.Application.Interfaces.IExpense;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;

[ApiController]
[Route("[Controller]")]
public class ExpenseController : ControllerBase
{
    private readonly IExpense expense;
    public ExpenseController(IExpense expense)
    {
        this.expense = expense;
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("admin-add-expense/{centerId}")]
    public async Task<IActionResult> AddExpense(int centerId, [FromBody] ExpenseRequestDto request)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await expense.AddExpenseAsync(centerId, userId, request);
        return Ok(result);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("admin-update-expense/{centerId}/{expenseId}")]
    public async Task<IActionResult> UpdateExpense(int centerId, int expenseId,[FromBody] UpdateExpenseRequestDto request)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await expense.UpdateExpenseAsync(centerId, userId, expenseId, request);
        return Ok(result);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("admin-delete-expense/{centerId}/{expenseId}")]
    public async Task<IActionResult> DeleteExpense(int centerId, int expenseId)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await expense.DeleteExpenseAsync(centerId, userId, expenseId);
        return Ok(result);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpGet("admin-get-expenses/{centerId}")]
    public async Task<IActionResult> GetExpenses(int centerId, [FromQuery] int year, [FromQuery] int month, [FromQuery] ExpenseSection? section, [FromQuery] PagedRequest request)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await expense.GetExpensesAsync(centerId, userId, year, month, section, request);
        return Ok(result);
    }
}
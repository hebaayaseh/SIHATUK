using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.MonthlySalaryDto;
using Sehatak.Application.Interfaces.ISalary;
using Sehatak.Domain.Entities.TenantEntities;

[ApiController]
[Route("[Controller]")]
public class SalaryController : ControllerBase
{
    private readonly ISalary salary;
    public SalaryController(ISalary salary)
    {
        this.salary = salary;
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("admin-set-category-salary/{centerId}")]
    public async Task<IActionResult> SetCategorySalary(int centerId, [FromBody] SetCategorySalaryRequestDto request)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await salary.SetCategorySalaryAsync(centerId, userId, request);
        return Ok(result);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("admin-set-doctor-salary/{centerId}")]
    public async Task<IActionResult> SetDoctorSalary(int centerId, [FromBody] SetDoctorSalaryRequestDto request)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await salary.SetDoctorSalaryAsync(centerId, userId, request);
        return Ok(result);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpGet("admin-get-salaries/{centerId}")]
    public async Task<IActionResult> GetSalaries(int centerId, [FromQuery] int year, [FromQuery] int month)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await salary.GetSalariesAsync(centerId, userId, year, month);
        return Ok(result);
    }
}
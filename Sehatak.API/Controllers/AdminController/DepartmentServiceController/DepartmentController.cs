using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.DepartmentDto;
using Sehatak.Application.Interfaces.DepartmentInterface;

namespace Sehatak.API.Controllers.SuperAdminAndAdmin.DepartmentServiceController
{
    [ApiController]
    [Route("api/[Controller]")]
    public class DepartmentController : ControllerBase
    {
        private readonly IDepartmentService departmentService;
        public DepartmentController(IDepartmentService departmentService)
        {
            this.departmentService = departmentService;
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpPost("add-department/{centerId}")]
        public async Task<IActionResult> AddDepartment(int centerId, [FromForm] DepartmentRequestDto request)
        {
            var result = await departmentService.AddDepartmentAsync(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpPost("edit-department/{centerId}")]
        public async Task<IActionResult> EditDepartment(int centerId, [FromForm] DepartmentUpdateRequestDto request)
        {
            var result = await departmentService.UpdateDepartmentAsync(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpDelete("delete-department/{centerId}")]
        public async Task<IActionResult> DeleteDepartment(int centerId, [FromBody] DepartmentRemoveRequestDto request)
        {
            var result = await departmentService.RemoveDepartmentAsync(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpGet("/{centerId}")]
        public async Task<IActionResult> GetDepartments(int centerId)
        {
            var result = await departmentService.GetDepartmentsAsync(centerId);
            return Ok(result);
        }
    }
}

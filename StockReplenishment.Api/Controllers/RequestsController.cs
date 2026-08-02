using Microsoft.AspNetCore.Mvc;
using StockReplenishment.Api.Models.Dto;
using StockReplenishment.Api.Services;

namespace StockReplenishment.Api.Controllers;

[ApiController]
[Route("api/requests")]
public class RequestsController : ControllerBase
{
    private readonly IReplenishmentService _service;

    public RequestsController(IReplenishmentService service)
    {
        _service = service;
    }

    /// <summary>Get all requests with optional filtering and pagination</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PaginatedResponse<RequestDto>>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? status = null,
        [FromQuery] string? priority = null,
        [FromQuery] string? location = null)
    {
        return Ok(await _service.GetAllAsync(page, pageSize, status, priority, location));
    }

    /// <summary>Get single request by ID</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<RequestDto>>> GetById(Guid id)
    {
        var dto = await _service.GetAsync(id);
        return Ok(new ApiResponse<RequestDto> { Success = true, Data = dto });
    }

    /// <summary>Create new request (Draft status)</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<RequestDto>>> Create([FromBody] RequestDto dto)
    {
        var created = await _service.CreateAsync(dto, User?.Identity?.Name ?? "System");
        return CreatedAtAction(nameof(GetById), new { id = created.Id },
            new ApiResponse<RequestDto> { Success = true, Data = created });
    }

    /// <summary>Update draft request</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<RequestDto>>> Update(Guid id, [FromBody] RequestDto dto)
    {
        var updated = await _service.UpdateAsync(id, dto);
        return Ok(new ApiResponse<RequestDto> { Success = true, Data = updated });
    }

    /// <summary>Delete draft request</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }

    /// <summary>Submit request for approval (triggers async validation)</summary>
    [HttpPost("{id}/submit")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ApiResponse<RequestDto>>> Submit(Guid id)
    {
        var submitted = await _service.SubmitAsync(id);
        return Accepted(Url.Action(nameof(GetValidationStatus), new { id }),
            new ApiResponse<RequestDto> { Success = true, Data = submitted });
    }

    /// <summary>Check validation status</summary>
    [HttpGet("{id}/validation-status")]
    public async Task<ActionResult<ApiResponse<ValidationStatusDto>>> GetValidationStatus(Guid id)
    {
        var status = await _service.GetValidationStatusAsync(id);
        return Ok(new ApiResponse<ValidationStatusDto> { Success = true, Data = status });
    }

    /// <summary>Approve request</summary>
    [HttpPost("{id}/approve")]
    public async Task<ActionResult<ApiResponse<RequestDto>>> Approve(Guid id)
    {
        var approved = await _service.ApproveAsync(id, User?.Identity?.Name ?? "System");
        return Ok(new ApiResponse<RequestDto> { Success = true, Data = approved });
    }

    /// <summary>Reject request</summary>
    [HttpPost("{id}/reject")]
    public async Task<ActionResult<ApiResponse<RequestDto>>> Reject(Guid id, [FromBody] ActionRequestDto dto)
    {
        var rejected = await _service.RejectAsync(id, dto.Reason ?? string.Empty);
        return Ok(new ApiResponse<RequestDto> { Success = true, Data = rejected });
    }

    /// <summary>Fulfill request</summary>
    [HttpPost("{id}/fulfill")]
    public async Task<ActionResult<ApiResponse<RequestDto>>> Fulfill(Guid id, [FromBody] ActionRequestDto dto)
    {
        var fulfilled = await _service.FulfillAsync(id, dto.FulfilledItems ?? new List<FulfillmentItem>());
        return Ok(new ApiResponse<RequestDto> { Success = true, Data = fulfilled });
    }
}

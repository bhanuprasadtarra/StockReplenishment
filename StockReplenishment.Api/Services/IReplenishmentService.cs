using StockReplenishment.Api.Models.Dto;

namespace StockReplenishment.Api.Services;

public interface IReplenishmentService
{
    // Query operations
    Task<RequestDto> GetAsync(Guid id);
    Task<ApiResponse<PaginatedResponse<RequestDto>>> GetAllAsync(
        int page = 1, int pageSize = 10,
        string? status = null, string? priority = null, string? location = null);

    // Create/Update operations
    Task<RequestDto> CreateAsync(RequestDto dto, string createdBy);
    Task<RequestDto> UpdateAsync(Guid id, RequestDto dto);
    Task DeleteAsync(Guid id);

    // Workflow operations
    Task<RequestDto> SubmitAsync(Guid id);
    Task<ValidationStatusDto> GetValidationStatusAsync(Guid id);
    Task<RequestDto> ApproveAsync(Guid id, string approvedBy);
    Task<RequestDto> RejectAsync(Guid id, string reason);
    Task<RequestDto> FulfillAsync(Guid id, List<FulfillmentItem> items);
}

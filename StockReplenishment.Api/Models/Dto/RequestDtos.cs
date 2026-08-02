using StockReplenishment.Api.Models.Domain;

namespace StockReplenishment.Api.Models.Dto;

// Single request DTO used for create, update, and response
public class RequestDto
{
    public Guid? Id { get; set; }  // Null when creating
    public string RequestNumber { get; set; } = string.Empty;
    public string StockLocation { get; set; } = string.Empty;
    public RequestPriority Priority { get; set; }
    public RequestStatus? Status { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? CreatedDate { get; set; }

    // Workflow response fields (null when creating)
    public DateTime? SubmittedDate { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? FulfilledDate { get; set; }

    // Validation response fields
    public ValidationStatus? ValidationStatus { get; set; }
    public string? ValidationError { get; set; }

    public string? Notes { get; set; }
    public List<LineItemDto> LineItems { get; set; } = new();
}

public class LineItemDto
{
    public Guid? Id { get; set; }
    public string ArticleNumber { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal FulfilledQuantity { get; set; }
    public string? Unit { get; set; }
}

// Generic API Response (reused for ALL responses)
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public List<string> Errors { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// Paginated response (reuses ApiResponse<T>)
public class PaginatedResponse<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalRecords { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
}

// Unified action request DTOs (single class, multiple purposes)
public class ActionRequestDto
{
    public string? Reason { get; set; }  // For rejection
    public List<FulfillmentItem>? FulfilledItems { get; set; }  // For fulfillment
}

public class FulfillmentItem
{
    public Guid LineItemId { get; set; }
    public decimal FulfilledQuantity { get; set; }
}

public class ValidationStatusDto
{
    public Guid JobId { get; set; }
    public ValidationStatus Status { get; set; }
    public bool? Result { get; set; }
    public string? Error { get; set; }
}

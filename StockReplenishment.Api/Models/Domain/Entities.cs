namespace StockReplenishment.Api.Models.Domain;

public class ReplenishmentRequest
{
    public Guid Id { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string StockLocation { get; set; } = string.Empty;
    public RequestPriority Priority { get; set; }
    public RequestStatus Status { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }

    // Workflow fields
    public DateTime? SubmittedDate { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? FulfilledDate { get; set; }

    // Validation fields
    public Guid? ValidationJobId { get; set; }
    public ValidationStatus? ValidationStatus { get; set; }
    public string? ValidationError { get; set; }

    public string? Notes { get; set; }
    public ICollection<RequestLineItem> LineItems { get; set; } = new List<RequestLineItem>();
}

public class RequestLineItem
{
    public Guid Id { get; set; }
    public Guid ReplenishmentRequestId { get; set; }
    public string ArticleNumber { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal FulfilledQuantity { get; set; }
    public string? Unit { get; set; }
}

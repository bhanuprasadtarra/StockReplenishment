using AutoMapper;
using Microsoft.EntityFrameworkCore;
using StockReplenishment.Api.Data;
using StockReplenishment.Api.Models.Domain;
using StockReplenishment.Api.Models.Dto;

namespace StockReplenishment.Api.Services;

public class ReplenishmentService : IReplenishmentService
{
    private readonly AppDbContext _db;
    private readonly IMapper _mapper;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReplenishmentService> _logger;

    public ReplenishmentService(
        AppDbContext db, IMapper mapper,
        IServiceScopeFactory scopeFactory,
        ILogger<ReplenishmentService> logger)
    {
        _db = db;
        _mapper = mapper;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // HELPER METHODS (Private, Reusable within this class)
    private async Task<ReplenishmentRequest> FindByIdOrThrowAsync(Guid id)
    {
        var request = await _db.Requests
            .Include(r => r.LineItems)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException($"Request {id} not found");
        return request;
    }

    // Keeps the workflow linear: Draft -> Submitted -> Approved/Rejected -> Fulfilled.
    // Anything not explicitly listed here is rejected, so there's no way to e.g. approve
    // a draft directly or fulfill something that was never approved.
    private static void ValidateStateTransition(RequestStatus current, RequestStatus target)
    {
        bool isValid = (current, target) switch
        {
            (RequestStatus.Draft, RequestStatus.Submitted) => true,
            (RequestStatus.Submitted, RequestStatus.Approved) => true,
            (RequestStatus.Submitted, RequestStatus.Rejected) => true,
            (RequestStatus.Approved, RequestStatus.Fulfilled) => true,
            _ => false
        };

        if (!isValid)
            throw new InvalidOperationException($"Cannot transition from {current} to {target}");
    }

    private static void ValidateLineItems(List<LineItemDto> items)
    {
        if (items == null || items.Count == 0)
            throw new ArgumentException("Request must have at least one line item");

        if (items.Any(i => i.RequestedQuantity <= 0))
            throw new ArgumentException("Quantities must be greater than zero");
    }

    // QUERY OPERATIONS
    public async Task<RequestDto> GetAsync(Guid id)
    {
        var request = await FindByIdOrThrowAsync(id);
        return _mapper.Map<RequestDto>(request);
    }

    public async Task<ApiResponse<PaginatedResponse<RequestDto>>> GetAllAsync(
        int page = 1, int pageSize = 10,
        string? status = null, string? priority = null, string? location = null)
    {
        var query = _db.Requests.Include(r => r.LineItems).AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<RequestStatus>(status, true, out var s))
            query = query.Where(r => r.Status == s);
        if (!string.IsNullOrEmpty(priority) && Enum.TryParse<RequestPriority>(priority, true, out var p))
            query = query.Where(r => r.Priority == p);
        if (!string.IsNullOrEmpty(location))
            query = query.Where(r => r.StockLocation == location);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = _mapper.Map<List<RequestDto>>(items);
        var paginated = new PaginatedResponse<RequestDto>
        {
            Items = dtos,
            TotalRecords = total,
            TotalPages = pageSize > 0 ? (total + pageSize - 1) / pageSize : 0,
            CurrentPage = page,
            PageSize = pageSize
        };

        return new ApiResponse<PaginatedResponse<RequestDto>>
        {
            Success = true,
            Data = paginated
        };
    }

    // CREATE/UPDATE OPERATIONS
    public async Task<RequestDto> CreateAsync(RequestDto dto, string createdBy)
    {
        ValidateLineItems(dto.LineItems);

        var request = new ReplenishmentRequest
        {
            Id = Guid.NewGuid(),
            RequestNumber = $"REP-{DateTime.UtcNow.Ticks}",
            StockLocation = dto.StockLocation,
            Priority = dto.Priority,
            Status = RequestStatus.Draft,
            CreatedBy = createdBy,
            CreatedDate = DateTime.UtcNow,
            Notes = dto.Notes,
            LineItems = dto.LineItems.Select(li => new RequestLineItem
            {
                Id = Guid.NewGuid(),
                ArticleNumber = li.ArticleNumber,
                Description = li.Description,
                RequestedQuantity = li.RequestedQuantity,
                Unit = li.Unit
            }).ToList()
        };

        _db.Requests.Add(request);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Created request {RequestNumber}", request.RequestNumber);
        return _mapper.Map<RequestDto>(request);
    }

    public async Task<RequestDto> UpdateAsync(Guid id, RequestDto dto)
    {
        var request = await FindByIdOrThrowAsync(id);

        if (request.Status != RequestStatus.Draft)
            throw new InvalidOperationException("Can only update draft requests");

        ValidateLineItems(dto.LineItems);

        request.StockLocation = dto.StockLocation;
        request.Priority = dto.Priority;
        request.Notes = dto.Notes;
        _db.LineItems.RemoveRange(request.LineItems.ToList());
        request.LineItems.Clear();
        foreach (var li in dto.LineItems)
        {
            _db.LineItems.Add(new RequestLineItem
            {
                Id = li.Id ?? Guid.NewGuid(),
                ReplenishmentRequestId = request.Id,
                ArticleNumber = li.ArticleNumber,
                Description = li.Description,
                RequestedQuantity = li.RequestedQuantity,
                Unit = li.Unit
            });
        }

        await _db.SaveChangesAsync();
        return _mapper.Map<RequestDto>(request);
    }

    public async Task DeleteAsync(Guid id)
    {
        var request = await FindByIdOrThrowAsync(id);

        if (request.Status != RequestStatus.Draft)
            throw new InvalidOperationException("Can only delete draft requests");

        _db.Requests.Remove(request);
        await _db.SaveChangesAsync();
    }

    // WORKFLOW OPERATIONS
    public async Task<RequestDto> SubmitAsync(Guid id)
    {
        var request = await FindByIdOrThrowAsync(id);
        ValidateStateTransition(request.Status, RequestStatus.Submitted);

        request.Status = RequestStatus.Submitted;
        request.SubmittedDate = DateTime.UtcNow;
        request.ValidationJobId = Guid.NewGuid();
        request.ValidationStatus = ValidationStatus.InProgress;

        await _db.SaveChangesAsync();

        // Validation happens in the background so Submit returns immediately instead of
        // making the caller wait 3-8 seconds for the "external stock system" to respond.
        // Can't just reuse _db/_mapper here though - they're scoped to this HTTP request and
        // will already be disposed by the time this task actually runs, so we spin up a fresh
        // DI scope instead. The UI picks up the result later via the validation-status endpoint.
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var validator = scope.ServiceProvider.GetRequiredService<IStockValidator>();
            await validator.ValidateAsync(id);
        });

        return _mapper.Map<RequestDto>(request);
    }

    public async Task<ValidationStatusDto> GetValidationStatusAsync(Guid id)
    {
        var request = await FindByIdOrThrowAsync(id);
        return new ValidationStatusDto
        {
            JobId = request.ValidationJobId ?? Guid.Empty,
            Status = request.ValidationStatus ?? ValidationStatus.NotStarted,
            Result = request.ValidationStatus == ValidationStatus.Completed
                ? string.IsNullOrEmpty(request.ValidationError)
                : null,
            Error = request.ValidationError
        };
    }

    public async Task<RequestDto> ApproveAsync(Guid id, string approvedBy)
    {
        var request = await FindByIdOrThrowAsync(id);
        ValidateStateTransition(request.Status, RequestStatus.Approved);

        request.Status = RequestStatus.Approved;
        request.ApprovedBy = approvedBy;
        request.ApprovedDate = DateTime.UtcNow;
        request.ValidationJobId = null;
        request.ValidationStatus = null;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Approved request {RequestNumber}", request.RequestNumber);
        return _mapper.Map<RequestDto>(request);
    }

    public async Task<RequestDto> RejectAsync(Guid id, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Rejection reason is required");

        var request = await FindByIdOrThrowAsync(id);
        ValidateStateTransition(request.Status, RequestStatus.Rejected);

        request.Status = RequestStatus.Rejected;
        request.RejectionReason = reason;
        request.ValidationJobId = null;
        request.ValidationStatus = null;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Rejected request {RequestNumber}: {Reason}", request.RequestNumber, reason);
        return _mapper.Map<RequestDto>(request);
    }

    public async Task<RequestDto> FulfillAsync(Guid id, List<FulfillmentItem> items)
    {
        var request = await FindByIdOrThrowAsync(id);
        ValidateStateTransition(request.Status, RequestStatus.Fulfilled);

        foreach (var item in items)
        {
            var lineItem = request.LineItems.FirstOrDefault(li => li.Id == item.LineItemId)
                ?? throw new KeyNotFoundException($"Line item {item.LineItemId} not found");
            lineItem.FulfilledQuantity = item.FulfilledQuantity;
        }

        request.Status = RequestStatus.Fulfilled;
        request.FulfilledDate = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return _mapper.Map<RequestDto>(request);
    }
}

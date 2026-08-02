using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StockReplenishment.Api.Data;
using StockReplenishment.Api.Mappings;
using StockReplenishment.Api.Models.Domain;
using StockReplenishment.Api.Models.Dto;
using StockReplenishment.Api.Services;

namespace StockReplenishment.Tests;

/// <summary>
/// Covers ReplenishmentService's request lifecycle (create, submit, approve, reject, fulfill, delete)
/// against an EF Core in-memory database, including the status-transition guards.
/// </summary>
public class ReplenishmentServiceTests
{
    private AppDbContext _db = null!;
    private ReplenishmentService _service = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAutoMapper(cfg => cfg.AddProfile<MappingProfile>());
        services.AddSingleton(Substitute.For<IStockValidator>());
        var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logger = Substitute.For<ILogger<ReplenishmentService>>();

        _service = new ReplenishmentService(_db, mapper, scopeFactory, logger);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    // Minimal valid payload; individual tests mutate a copy to trigger validation failures.
    private static RequestDto ValidDto() => new()
    {
        StockLocation = "Test",
        Priority = RequestPriority.Normal,
        LineItems = new() { new LineItemDto { ArticleNumber = "TEST-001", RequestedQuantity = 10, Unit = "pcs" } }
    };

    // Seeds a request directly into the DB (bypassing the service) so tests can start
    // from an arbitrary status without depending on CreateAsync's own behavior.
    private async Task<ReplenishmentRequest> SeedRequestAsync(RequestStatus status)
    {
        var request = new ReplenishmentRequest
        {
            Id = Guid.NewGuid(),
            RequestNumber = $"REP-{Guid.NewGuid():N}",
            StockLocation = "Test",
            Priority = RequestPriority.Normal,
            Status = status,
            CreatedBy = "tester",
            CreatedDate = DateTime.UtcNow,
            LineItems = new List<RequestLineItem>
            {
                new() { Id = Guid.NewGuid(), ArticleNumber = "TEST", RequestedQuantity = 10, Unit = "pcs" }
            }
        };
        _db.Requests.Add(request);
        await _db.SaveChangesAsync();
        return request;
    }

    [Test]
    public async Task Create_WithValidData_ReturnsRequest()
    {
        var result = await _service.CreateAsync(ValidDto(), "user");
        Assert.That(result.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(result.Status, Is.EqualTo(RequestStatus.Draft));
    }

    [Test]
    public void Create_NoLineItems_ThrowsException()
    {
        var dto = new RequestDto { StockLocation = "Test", LineItems = new() };
        Assert.ThrowsAsync<ArgumentException>(async () => await _service.CreateAsync(dto, "user"));
    }

    [Test]
    public void Create_ZeroQuantity_ThrowsException()
    {
        var dto = ValidDto();
        dto.LineItems[0].RequestedQuantity = 0;
        Assert.ThrowsAsync<ArgumentException>(async () => await _service.CreateAsync(dto, "user"));
    }

    [Test]
    public async Task Submit_ChangesStatusToSubmitted()
    {
        var request = await SeedRequestAsync(RequestStatus.Draft);
        var result = await _service.SubmitAsync(request.Id);
        Assert.That(result.Status, Is.EqualTo(RequestStatus.Submitted));
        Assert.That(result.ValidationStatus, Is.EqualTo(ValidationStatus.InProgress));
    }

    // Only Draft requests may be submitted; an already-Approved request must reject re-submission.
    [Test]
    public async Task Submit_FromApproved_ThrowsInvalidOperation()
    {
        var request = await SeedRequestAsync(RequestStatus.Approved);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _service.SubmitAsync(request.Id));
    }

    [Test]
    public async Task Approve_FromSubmitted_ChangesStatus()
    {
        var request = await SeedRequestAsync(RequestStatus.Submitted);
        var result = await _service.ApproveAsync(request.Id, "manager");
        Assert.That(result.Status, Is.EqualTo(RequestStatus.Approved));
        Assert.That(result.ApprovedBy, Is.EqualTo("manager"));
    }

    [Test]
    public async Task Reject_WithoutReason_ThrowsArgumentException()
    {
        var request = await SeedRequestAsync(RequestStatus.Submitted);
        Assert.ThrowsAsync<ArgumentException>(async () => await _service.RejectAsync(request.Id, ""));
    }

    [Test]
    public async Task Reject_WithReason_ChangesStatus()
    {
        var request = await SeedRequestAsync(RequestStatus.Submitted);
        var result = await _service.RejectAsync(request.Id, "No budget");
        Assert.That(result.Status, Is.EqualTo(RequestStatus.Rejected));
        Assert.That(result.RejectionReason, Is.EqualTo("No budget"));
    }

    [Test]
    public async Task Fulfill_FromApproved_UpdatesLineItemsAndStatus()
    {
        var request = await SeedRequestAsync(RequestStatus.Approved);
        var lineItemId = request.LineItems.First().Id;

        var result = await _service.FulfillAsync(request.Id, new List<FulfillmentItem>
        {
            new() { LineItemId = lineItemId, FulfilledQuantity = 10 }
        });

        Assert.That(result.Status, Is.EqualTo(RequestStatus.Fulfilled));
        Assert.That(result.LineItems.First().FulfilledQuantity, Is.EqualTo(10));
    }

    // Deletion is restricted to Draft requests to prevent removing requests already in the approval workflow.
    [Test]
    public async Task Delete_NonDraft_ThrowsInvalidOperation()
    {
        var request = await SeedRequestAsync(RequestStatus.Submitted);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _service.DeleteAsync(request.Id));
    }

    [Test]
    public async Task Delete_Draft_RemovesRequest()
    {
        var request = await SeedRequestAsync(RequestStatus.Draft);
        await _service.DeleteAsync(request.Id);
        Assert.ThrowsAsync<KeyNotFoundException>(async () => await _service.GetAsync(request.Id));
    }

    [Test]
    public void Get_NonExistent_ThrowsKeyNotFound()
    {
        Assert.ThrowsAsync<KeyNotFoundException>(async () => await _service.GetAsync(Guid.NewGuid()));
    }

    [Test]
    public async Task GetAll_FiltersByStatus()
    {
        await SeedRequestAsync(RequestStatus.Draft);
        await SeedRequestAsync(RequestStatus.Approved);

        var result = await _service.GetAllAsync(status: "Approved");

        Assert.That(result.Data!.Items, Has.All.Matches<RequestDto>(r => r.Status == RequestStatus.Approved));
    }
}

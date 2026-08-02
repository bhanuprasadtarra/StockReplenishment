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
    private IStockValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        // Kept as a field (rather than only registered inline) so individual tests can
        // reconfigure its behavior - e.g. simulating a slow validation call.
        _validator = Substitute.For<IStockValidator>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAutoMapper(cfg => cfg.AddProfile<MappingProfile>());
        services.AddSingleton(_validator);
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
    // location/priority/createdDate are optional so existing callers are unaffected; filter
    // and pagination-ordering tests need to control them explicitly.
    private async Task<ReplenishmentRequest> SeedRequestAsync(
        RequestStatus status, string location = "Test", RequestPriority priority = RequestPriority.Normal, DateTime? createdDate = null)
    {
        var request = new ReplenishmentRequest
        {
            Id = Guid.NewGuid(),
            RequestNumber = $"REP-{Guid.NewGuid():N}",
            StockLocation = location,
            Priority = priority,
            Status = status,
            CreatedBy = "tester",
            CreatedDate = createdDate ?? DateTime.UtcNow,
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

    [Test]
    public async Task GetAll_FilterByLocation_ReturnsOnlyMatchingLocation()
    {
        await SeedRequestAsync(RequestStatus.Draft, location: "Station-A");
        await SeedRequestAsync(RequestStatus.Draft, location: "Station-B");

        var result = await _service.GetAllAsync(location: "Station-A");

        Assert.That(result.Data!.Items, Has.All.Matches<RequestDto>(r => r.StockLocation == "Station-A"));
        Assert.That(result.Data!.Items, Has.Exactly(1).Items);
    }

    // Complements GetAll_FiltersByStatus by also asserting the non-matching request is
    // excluded, not just that whatever comes back happens to match.
    [Test]
    public async Task GetAll_FilterByStatus_ReturnsOnlyMatchingStatus()
    {
        var draft = await SeedRequestAsync(RequestStatus.Draft);
        var approved = await SeedRequestAsync(RequestStatus.Approved);

        var result = await _service.GetAllAsync(status: "Approved");

        Assert.That(result.Data!.Items.Select(r => r.Id), Does.Contain(approved.Id));
        Assert.That(result.Data!.Items.Select(r => r.Id), Does.Not.Contain(draft.Id));
    }

    [Test]
    public async Task GetAll_FilterByPriority_ReturnsOnlyMatchingPriority()
    {
        await SeedRequestAsync(RequestStatus.Draft, priority: RequestPriority.Urgent);
        await SeedRequestAsync(RequestStatus.Draft, priority: RequestPriority.Low);

        var result = await _service.GetAllAsync(priority: "Urgent");

        Assert.That(result.Data!.Items, Has.All.Matches<RequestDto>(r => r.Priority == RequestPriority.Urgent));
        Assert.That(result.Data!.Items, Has.Exactly(1).Items);
    }

    [Test]
    public async Task GetAll_WithPagination_ReturnsPaginatedResults()
    {
        for (int i = 0; i < 5; i++)
            await SeedRequestAsync(RequestStatus.Draft);

        var result = await _service.GetAllAsync(page: 1, pageSize: 2);

        Assert.That(result.Data!.Items, Has.Exactly(2).Items);
        Assert.That(result.Data!.TotalRecords, Is.EqualTo(5));
        Assert.That(result.Data!.TotalPages, Is.EqualTo(3));
    }

    [Test]
    public async Task GetAll_SecondPage_ReturnsCorrectPage()
    {
        // Distinct CreatedDate per request makes page order deterministic - GetAllAsync
        // sorts newest first, so page 2 (size 2) should be the 3rd/4th most recent.
        var baseTime = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
            await SeedRequestAsync(RequestStatus.Draft, location: $"Loc-{i}", createdDate: baseTime.AddMinutes(i));

        var result = await _service.GetAllAsync(page: 2, pageSize: 2);

        Assert.That(result.Data!.Items.Select(r => r.StockLocation), Is.EqualTo(new[] { "Loc-2", "Loc-1" }));
    }

    [Test]
    public async Task Submit_ReturnsImmediately_DoesNotBlockOnValidation()
    {
        // Make the mocked external validator hang well past a reasonable request timeout -
        // if SubmitAsync awaited it directly instead of firing it via Task.Run, this test
        // would take just as long instead of returning almost instantly.
        _validator.ValidateAsync(Arg.Any<Guid>()).Returns(async _ => await Task.Delay(5000));
        var request = await SeedRequestAsync(RequestStatus.Draft);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _service.SubmitAsync(request.Id);
        sw.Stop();

        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000));
    }

    [Test]
    public async Task Update_DraftRequest_SucceedsAndUpdatesData()
    {
        var request = await SeedRequestAsync(RequestStatus.Draft);
        var dto = ValidDto();
        dto.StockLocation = "Updated-Location";
        dto.Priority = RequestPriority.Urgent;

        var result = await _service.UpdateAsync(request.Id, dto);

        Assert.That(result.StockLocation, Is.EqualTo("Updated-Location"));
        Assert.That(result.Priority, Is.EqualTo(RequestPriority.Urgent));
    }

    // Only Draft requests may be edited, mirroring the Delete guard - once submitted, a
    // request is in the approval workflow and shouldn't change under the reviewer.
    [Test]
    public async Task Update_NonDraftRequest_ThrowsException()
    {
        var request = await SeedRequestAsync(RequestStatus.Submitted);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _service.UpdateAsync(request.Id, ValidDto()));
    }
}

using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using StockReplenishment.Api.Controllers;
using StockReplenishment.Api.Models.Domain;
using StockReplenishment.Api.Models.Dto;
using StockReplenishment.Api.Services;

namespace StockReplenishment.Tests;

/// <summary>
/// Verifies RequestsController maps IReplenishmentService results/calls to the expected
/// ActionResult types and forwards arguments correctly; the service itself is mocked.
/// </summary>
public class RequestsControllerTests
{
    private IReplenishmentService _service = null!;
    private RequestsController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _service = Substitute.For<IReplenishmentService>();
        _controller = new RequestsController(_service);
    }

    private static RequestDto SampleDto(Guid id) => new()
    {
        Id = id,
        RequestNumber = "REP-0001",
        StockLocation = "Test",
        Status = RequestStatus.Draft,
        LineItems = new() { new LineItemDto { ArticleNumber = "A1", RequestedQuantity = 5 } }
    };

    [Test]
    public async Task GetById_ReturnsOkWithData()
    {
        var id = Guid.NewGuid();
        _service.GetAsync(id).Returns(SampleDto(id));

        var result = await _controller.GetById(id);

        var ok = result.Result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var body = ok!.Value as ApiResponse<RequestDto>;
        Assert.That(body!.Success, Is.True);
        Assert.That(body.Data!.Id, Is.EqualTo(id));
    }

    // Create should return 201 with a Location pointing back at the GetById action.
    [Test]
    public async Task Create_ReturnsCreatedAtAction()
    {
        var id = Guid.NewGuid();
        var dto = SampleDto(id);
        _service.CreateAsync(Arg.Any<RequestDto>(), Arg.Any<string>()).Returns(dto);

        var result = await _controller.Create(dto);

        var created = result.Result as CreatedAtActionResult;
        Assert.That(created, Is.Not.Null);
        Assert.That(created!.ActionName, Is.EqualTo(nameof(RequestsController.GetById)));
    }

    [Test]
    public async Task Delete_ReturnsNoContent()
    {
        var id = Guid.NewGuid();
        var result = await _controller.Delete(id);
        Assert.That(result, Is.InstanceOf<NoContentResult>());
        await _service.Received(1).DeleteAsync(id);
    }

    [Test]
    public async Task Approve_ReturnsOkWithUpdatedStatus()
    {
        var id = Guid.NewGuid();
        var dto = SampleDto(id);
        dto.Status = RequestStatus.Approved;
        _service.ApproveAsync(id, Arg.Any<string>()).Returns(dto);

        var result = await _controller.Approve(id);

        var ok = result.Result as OkObjectResult;
        var body = ok!.Value as ApiResponse<RequestDto>;
        Assert.That(body!.Data!.Status, Is.EqualTo(RequestStatus.Approved));
    }

    [Test]
    public async Task Reject_PassesReasonToService()
    {
        var id = Guid.NewGuid();
        var dto = SampleDto(id);
        dto.Status = RequestStatus.Rejected;
        _service.RejectAsync(id, "bad").Returns(dto);

        var result = await _controller.Reject(id, new ActionRequestDto { Reason = "bad" });

        var ok = result.Result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        await _service.Received(1).RejectAsync(id, "bad");
    }

    [Test]
    public async Task GetAll_ReturnsServiceResponse()
    {
        var response = new ApiResponse<PaginatedResponse<RequestDto>>
        {
            Success = true,
            Data = new PaginatedResponse<RequestDto> { Items = new() { SampleDto(Guid.NewGuid()) } }
        };
        _service.GetAllAsync(1, 10, null, null, null).Returns(response);

        var result = await _controller.GetAll();

        var ok = result.Result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        Assert.That(ok!.Value, Is.EqualTo(response));
    }
}

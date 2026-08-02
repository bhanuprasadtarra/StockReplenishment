using Microsoft.EntityFrameworkCore;
using StockReplenishment.Api.Data;
using StockReplenishment.Api.Models.Domain;

namespace StockReplenishment.Api.Services;

public class StockValidator : IStockValidator
{
    private readonly AppDbContext _db;
    private readonly ILogger<StockValidator> _logger;

    public StockValidator(AppDbContext db, ILogger<StockValidator> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ValidateAsync(Guid requestId)
    {
        try
        {
            // There's no real warehouse/ERP system to check stock against, so this stands in
            // for one - a random delay to mimic network latency and an 80% pass rate so the
            // "stock not available" path is actually reachable during a demo, not just theory.
            await Task.Delay(3000 + Random.Shared.Next(0, 5000));

            var request = await _db.Requests
                .Include(r => r.LineItems)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (request == null) return; // request got deleted while validation was running

            var success = Random.Shared.Next(0, 100) > 20;  // ~80% success rate

            request.ValidationStatus = ValidationStatus.Completed;
            request.ValidationError = success ? null : "Stock not available for some items";

            await _db.SaveChangesAsync();
            _logger.LogInformation("Validation completed for {RequestNumber}: {Result}", request.RequestNumber, success ? "Success" : "Failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed for request {RequestId}", requestId);
        }
    }
}

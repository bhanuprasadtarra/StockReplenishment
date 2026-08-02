using Microsoft.EntityFrameworkCore;
using StockReplenishment.Api.Models.Domain;

namespace StockReplenishment.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ReplenishmentRequest> Requests { get; set; } = null!;
    public DbSet<RequestLineItem> LineItems { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Line items always belong to exactly one request, so cascade delete is fine here
        modelBuilder.Entity<ReplenishmentRequest>()
            .HasMany(r => r.LineItems)
            .WithOne()
            .HasForeignKey(li => li.ReplenishmentRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ReplenishmentRequest>()
            .HasIndex(r => r.RequestNumber)
            .IsUnique();

        SeedData(modelBuilder);
    }

    // Seeds ~20 requests spread across every status/priority so the UI has something
    // realistic to look at (filters, pagination, workflow buttons) without manual setup.
    private static void SeedData(ModelBuilder modelBuilder)
    {
        var requests = new List<ReplenishmentRequest>();
        var lineItems = new List<RequestLineItem>();
        var locations = new[] { "Station-A", "Station-B", "Warehouse-Main", "Packaging" };
        var statuses = new[] { RequestStatus.Draft, RequestStatus.Submitted, RequestStatus.Approved, RequestStatus.Rejected, RequestStatus.Fulfilled };
        var priorities = new[] { RequestPriority.Low, RequestPriority.Normal, RequestPriority.Urgent };

        for (int i = 1; i <= 20; i++)
        {
            var status = statuses[i % statuses.Length];
            var requestId = Guid.NewGuid();
            var request = new ReplenishmentRequest
            {
                Id = requestId,
                RequestNumber = $"REP-{i:D4}",
                StockLocation = locations[i % locations.Length],
                Priority = priorities[i % priorities.Length],
                Status = status,
                CreatedBy = $"user{i % 3}",
                // HasData needs fixed values, not DateTime.UtcNow - otherwise EF thinks the model
                // changes on every build and keeps asking for a new migration
                CreatedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-i),
                SubmittedDate = status != RequestStatus.Draft ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-i + 1) : null,
                ApprovedBy = status == RequestStatus.Approved || status == RequestStatus.Fulfilled ? "manager1" : null,
                ApprovedDate = status == RequestStatus.Approved || status == RequestStatus.Fulfilled ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-i + 2) : null,
                RejectionReason = status == RequestStatus.Rejected ? "Insufficient budget" : null,
                FulfilledDate = status == RequestStatus.Fulfilled ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-i + 3) : null,
                Notes = $"Request #{i}",
                ValidationStatus = status == RequestStatus.Submitted ? ValidationStatus.InProgress : null
            };
            requests.Add(request);

            // Line items go into their own HasData() call with an explicit FK rather than
            // being attached to request.LineItems - EF's seeding API doesn't support nested
            // navigation collections, everything has to be flattened out like this
            lineItems.Add(new RequestLineItem
            {
                Id = Guid.NewGuid(),
                ReplenishmentRequestId = requestId,
                ArticleNumber = $"BOLT-M8-{i:D2}",
                Description = $"M8 Bolt {i}",
                RequestedQuantity = 100,
                FulfilledQuantity = status == RequestStatus.Fulfilled ? 100 : 0,
                Unit = "pcs"
            });
            lineItems.Add(new RequestLineItem
            {
                Id = Guid.NewGuid(),
                ReplenishmentRequestId = requestId,
                ArticleNumber = $"GASKET-{i:D2}",
                Description = $"Gasket {i}",
                RequestedQuantity = 50,
                FulfilledQuantity = status == RequestStatus.Fulfilled ? 50 : 0,
                Unit = "pcs"
            });
        }

        modelBuilder.Entity<ReplenishmentRequest>().HasData(requests);
        modelBuilder.Entity<RequestLineItem>().HasData(lineItems);
    }
}

using Microsoft.EntityFrameworkCore;
using StockReplenishment.Api.Data;
using StockReplenishment.Api.Mappings;
using StockReplenishment.Api.Middleware;
using StockReplenishment.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Stock Replenishment API", Version = "v1" });
});

/* Named in-memory DB so every scope (including the background validation task) talks
 to the same store instead of getting its own empty one */
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseInMemoryDatabase("StockReplenishment"));

builder.Services.AddAutoMapper(cfg => cfg.AddProfile<MappingProfile>());
builder.Services.AddScoped<IReplenishmentService, ReplenishmentService>();
builder.Services.AddScoped<IStockValidator, StockValidator>();

// Wide-open CORS is fine for this assignment - the Blazor app calls this API server-to-server
// anyway (Blazor Server renders on the server, not in the browser), this is just so Swagger/
// Postman/whatever also work without fuss during dev
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// EnsureCreated() triggers the HasData() seed on first run. Fine for an in-memory DB;
// a real SQL backend would use migrations instead.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseMiddleware<ErrorHandler>();
app.UseAuthorization();
app.MapControllers();
app.Run();

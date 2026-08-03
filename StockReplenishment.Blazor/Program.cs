using MudBlazor.Services;
using StockReplenishment.Blazor.Components;
using StockReplenishment.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// This app has no data access of its own - every page goes through ApiClient to the
// separate StockReplenishment.Api project (the ProjectReference is only there for the
// shared DTOs, not to call the service layer in-process).
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7184/";
builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

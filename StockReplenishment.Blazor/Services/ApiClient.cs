using System.Net.Http.Json;
using StockReplenishment.Api.Models.Dto;

namespace StockReplenishment.Blazor.Services;

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http) => _http = http;

    public async Task<RequestDto?> GetAsync(Guid id)
    {
        var response = await _http.GetFromJsonAsync<ApiResponse<RequestDto>>($"api/requests/{id}");
        return response?.Data;
    }

    public async Task<PaginatedResponse<RequestDto>?> GetAllAsync(
        int page = 1, int pageSize = 10,
        string? status = null, string? priority = null, string? location = null)
    {
        var query = $"api/requests?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(status)) query += $"&status={status}";
        if (!string.IsNullOrEmpty(priority)) query += $"&priority={priority}";
        if (!string.IsNullOrEmpty(location)) query += $"&location={location}";

        var response = await _http.GetFromJsonAsync<ApiResponse<PaginatedResponse<RequestDto>>>(query);
        return response?.Data;
    }

    public async Task<RequestDto?> CreateAsync(RequestDto dto)
    {
        var response = await _http.PostAsJsonAsync("api/requests", dto);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<RequestDto>>();
        return body?.Data;
    }

    public async Task<RequestDto?> UpdateAsync(Guid id, RequestDto dto)
    {
        var response = await _http.PutAsJsonAsync($"api/requests/{id}", dto);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<RequestDto>>();
        return body?.Data;
    }

    public async Task DeleteAsync(Guid id) =>
        await _http.DeleteAsync($"api/requests/{id}");

    public async Task<RequestDto?> SubmitAsync(Guid id)
    {
        var response = await _http.PostAsync($"api/requests/{id}/submit", null);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<RequestDto>>();
        return body?.Data;
    }

    public async Task<ValidationStatusDto?> GetValidationStatusAsync(Guid id)
    {
        var response = await _http.GetFromJsonAsync<ApiResponse<ValidationStatusDto>>($"api/requests/{id}/validation-status");
        return response?.Data;
    }

    public async Task<RequestDto?> ApproveAsync(Guid id)
    {
        var response = await _http.PostAsync($"api/requests/{id}/approve", null);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<RequestDto>>();
        return body?.Data;
    }

    public async Task<RequestDto?> RejectAsync(Guid id, string reason)
    {
        var response = await _http.PostAsJsonAsync($"api/requests/{id}/reject", new ActionRequestDto { Reason = reason });
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<RequestDto>>();
        return body?.Data;
    }

    public async Task<RequestDto?> FulfillAsync(Guid id, List<FulfillmentItem> items)
    {
        var response = await _http.PostAsJsonAsync($"api/requests/{id}/fulfill", new ActionRequestDto { FulfilledItems = items });
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<RequestDto>>();
        return body?.Data;
    }
}

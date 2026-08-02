namespace StockReplenishment.Api.Services;

public interface IStockValidator
{
    Task ValidateAsync(Guid requestId);
}

namespace Velotech.API.Dtos;

public class UpsertStoreProductDto
{
    public int StoreId { get; set; }
    public int ProductId { get; set; }

    public int StockSale { get; set; }
    public int StockRental { get; set; }
}
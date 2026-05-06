namespace Velotech.API.Dtos;

public class StoreProductDetailsDto
{
    public int StoreId { get; set; }
    public string StoreName { get; set; } = "";

    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string ProductType { get; set; } = "";

    public int StockSale { get; set; }
    public int StockRental { get; set; }

    public decimal PriceSale { get; set; } 
}
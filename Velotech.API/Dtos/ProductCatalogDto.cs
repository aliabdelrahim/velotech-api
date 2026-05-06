namespace Velotech.API.Dtos;

public class ProductCatalogDto
{
    public int ProductId { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public decimal PriceSale { get; set; }
    public decimal? PriceRental { get; set; }
    public bool IsRentable { get; set; }
    public int StockSale { get; set; }
    public int StockRental { get; set; }
}

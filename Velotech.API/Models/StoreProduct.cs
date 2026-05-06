namespace Velotech.API.Models;

public class StoreProduct
{
    public int StoreId { get; set; }
    public Store? Store { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int StockSale { get; set; }
    public int StockRental { get; set; }
}
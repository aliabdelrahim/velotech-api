namespace Velotech.API.Models;

public class Product
{
    public int Id { get; set; }

    public string? Name { get; set; }
    public string? Type { get; set; } // "Bike" ou "Accessory"

    public decimal PriceSale { get; set; }
    public decimal? PriceRental { get; set; }
    public List<StoreProduct>? StoreProducts { get; set; }

    public bool IsRentable { get; set; }
}

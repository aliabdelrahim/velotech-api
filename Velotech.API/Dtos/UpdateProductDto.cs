namespace Velotech.API.Dtos;

public class UpdateProductDto
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Accessory";
    public decimal PriceSale { get; set; }
    public decimal? PriceRental { get; set; }
    public bool IsRentable { get; set; }
}
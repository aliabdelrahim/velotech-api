namespace Velotech.API.Dtos;

public class ProductDetailsDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal PriceSale { get; set; }
    public decimal? PriceRental { get; set; }
    public bool IsRentable { get; set; }
}

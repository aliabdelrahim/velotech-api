namespace Velotech.API.Dtos;

public class CreateProductDto
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Accessory"; // "Bike" ou "Accessory"

    public decimal PriceSale { get; set; }          // obligatoire
    public decimal? PriceRental { get; set; }       // seulement si IsRentable=true
    public bool IsRentable { get; set; }            // true pour vélo louable
}

namespace Velotech.API.Dtos;

public class ProductDetailsDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal PriceSale { get; set; }
    public decimal? PriceRental { get; set; }
    public bool IsRentable { get; set; }
    public string? ImageUrls { get; set; }

    /// <summary>
    /// Attribution du produit aux magasins (back-office).
    /// Peuplee par GET /api/products/{id} pour pre-remplir le form d'edition.
    /// Peut etre vide si le produit n'est attribue a aucun magasin.
    /// </summary>
    public List<ProductStoreStockDto> StoreStocks { get; set; } = new();
}

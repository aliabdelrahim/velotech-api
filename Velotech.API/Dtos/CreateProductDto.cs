namespace Velotech.API.Dtos;

public class CreateProductDto
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Accessory"; // "Bike" ou "Accessory"

    public decimal PriceSale { get; set; }          // obligatoire
    public decimal? PriceRental { get; set; }       // seulement si IsRentable=true
    public bool IsRentable { get; set; }            // true pour vélo louable

    // URL d'une image externe illustrant le produit (optionnel).
    public string? ImageUrls { get; set; }

    /// <summary>
    /// Attribution du produit aux magasins avec stocks par magasin.
    /// Si null ou vide, le produit n'est attribue a aucun magasin
    /// (il n'apparaitra dans aucun catalogue tant qu'on n'aura pas
    /// cree des StoreProducts via l'endpoint dedie).
    /// </summary>
    public List<ProductStoreStockDto>? StoreStocks { get; set; }
}

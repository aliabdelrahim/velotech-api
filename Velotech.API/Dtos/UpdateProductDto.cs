namespace Velotech.API.Dtos;

public class UpdateProductDto
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Accessory";
    public decimal PriceSale { get; set; }
    public decimal? PriceRental { get; set; }
    public bool IsRentable { get; set; }
    public string? ImageUrls { get; set; }

    /// <summary>
    /// Nouvelle liste complete des attributions aux magasins.
    /// Mode "replace" : les StoreProducts existants pour ce produit
    /// sont alignes sur cette liste (ajout, modification, suppression).
    /// Si null, les stocks ne sont PAS modifies (seulement les infos produit).
    /// </summary>
    public List<ProductStoreStockDto>? StoreStocks { get; set; }
}

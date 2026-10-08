namespace Velotech.API.Dtos;

/// <summary>
/// Disponibilite d'un produit dans un magasin donne.
/// Retourne par GET /api/catalog/product/{productId}/stores
/// pour alimenter le bloc "Disponible dans ces magasins" sur la fiche produit.
/// </summary>
public class StoreAvailabilityDto
{
    public int StoreId { get; set; }
    public string? StoreName { get; set; }
    public string? Address { get; set; }
    public int StockSale { get; set; }
    public int StockRental { get; set; }
}

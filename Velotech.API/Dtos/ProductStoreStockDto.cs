namespace Velotech.API.Dtos;

/// <summary>
/// Represente le stock d'un produit dans un magasin, utilise :
///  - en entree (Create/Update Product) pour attribuer un produit aux magasins
///  - en sortie (ProductDetailsDto.StoreStocks) pour pre-remplir le form d'edition
/// </summary>
public class ProductStoreStockDto
{
    public int StoreId { get; set; }
    public string? StoreName { get; set; } // peuplee uniquement en reponse
    public int StockSale { get; set; }
    public int StockRental { get; set; }
}

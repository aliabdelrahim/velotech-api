namespace Velotech.API.Models;

public class Product : ISoftDeletable
{
    public int Id { get; set; }

    public string? Name { get; set; }
    public string? Type { get; set; } // "Bike" ou "Accessory"

    public decimal PriceSale { get; set; }
    public decimal? PriceRental { get; set; }
    public List<StoreProduct>? StoreProducts { get; set; }

    public bool IsRentable { get; set; }

    // URLs des images produit, concatenees et separees par des virgules.
    // Ex: "https://a.jpg,https://b.jpg,https://c.jpg"
    // La premiere URL est l'image principale (cards catalogue, panier, etc.),
    // toutes les URLs forment la galerie sur la fiche produit.
    // Null = pas d'image, le front affiche un placeholder SVG par defaut.
    // Choix d'implementation : CSV plutot qu'une table ProductImages dediee,
    // suffisant pour un prototype. En production, une table avec DisplayOrder
    // et Caption permettrait une gestion plus fine.
    public string? ImageUrls { get; set; }

    // Soft delete : la ligne reste en base, mais est exclue des requetes.
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}

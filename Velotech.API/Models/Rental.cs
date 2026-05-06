namespace Velotech.API.Models;

public class Rental
{
    public int Id { get; set; }

    // Client
    public int UserId { get; set; }
    public User? User { get; set; }

    // Magasin où la location est faite
    public int StoreId { get; set; }
    public Store? Store { get; set; }

    // Produit loué (normalement un vélo)
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public decimal TotalPrice { get; set; }

    // "Pending", "Confirmed", "Active", "Completed", "Cancelled"
    public string Status { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
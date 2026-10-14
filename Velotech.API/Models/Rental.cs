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

    // "PendingPayment", "Confirmed", "Active", "Completed", "Cancelled"
    // Creee en PendingPayment (velo reserve), Confirmed apres paiement. Annulee
    // automatiquement si elle n'est pas payee sous 30 min (PendingPaymentCleanupService).
    public string Status { get; set; } = "PendingPayment";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
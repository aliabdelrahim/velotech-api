namespace Velotech.API.Models;

public class Appointment
{
    public int Id { get; set; }

    // Client
    public int UserId { get; set; }
    public User? User { get; set; }

    // Magasin concerné
    public int StoreId { get; set; }
    public Store? Store { get; set; }

    // Vélo concerné (optionnel)
    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    // Type : "Entretien", "Réparation", etc.
    public string ServiceType { get; set; } = "Entretien";

    public DateTime ScheduledAt { get; set; }

    public string? Notes { get; set; }

    // "Pending", "Confirmed", "Completed", "Cancelled"
    public string Status { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}